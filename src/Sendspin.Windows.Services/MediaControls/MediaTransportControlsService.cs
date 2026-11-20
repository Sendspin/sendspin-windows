using Microsoft.Extensions.Logging;
using Sendspin.SDK.Models;
using Sendspin.SDK.Protocol.Messages;
using Sendspin.Windows.Services.Playback;
using Windows.Media;
using Windows.Storage.Streams;

namespace Sendspin.Windows.Services.MediaControls;

public sealed class MediaTransportControlsService : IMediaTransportControlsService
{
    private readonly ILogger<MediaTransportControlsService> _logger;
    private SystemMediaTransportControls? _smtc;
    private SystemMediaTransportControlsDisplayUpdater? _displayUpdater;
    private SystemMediaTransportControlsTimelineProperties? _timeline;
    private bool _isEnabled = true;
    private bool _disposed;

    // Held so Initialize can apply a list reported before the window handle existed.
    private IReadOnlyCollection<string>? _supportedCommands;

    public MediaTransportControlsService(ILogger<MediaTransportControlsService> logger)
    {
        _logger = logger;
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            if (_smtc != null)
            {
                _smtc.IsEnabled = value;
            }

            _logger.LogInformation("SMTC integration toggled: {Enabled}", value);
        }
    }

    public event EventHandler? PlayPauseRequested;

    public event EventHandler? NextRequested;

    public event EventHandler? PreviousRequested;

    public void Initialize(IntPtr windowHandle)
    {
        if (_smtc != null)
        {
            return;
        }

        try
        {
            _smtc = SystemMediaTransportControlsInterop.GetForWindow(windowHandle);
            _smtc.IsEnabled = _isEnabled;
            ApplySupportedCommands(_smtc);
            _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
            _smtc.ButtonPressed += OnButtonPressed;

            // Timeline properties (position + duration) are pushed on demand via
            // UpdateTimeline; only StartTime is fixed here. No seek range is
            // reported: this client cannot seek on its own (only the group's
            // controller can), and we do not handle PlaybackPositionChangeRequested
            // — the OS still marks a session with a timeline as seekable, so a
            // user dragging the overlay's seek bar sends a request we ignore.
            // Wiring the event to a seek command (when acting as controller) is
            // left as follow-up work.
            _timeline = new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero
            };

            _displayUpdater = _smtc.DisplayUpdater;
            _displayUpdater.Type = MediaPlaybackType.Music;
            _displayUpdater.Update();

            _logger.LogInformation("System Media Transport Controls initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize System Media Transport Controls");
            _smtc = null;
            _displayUpdater = null;
        }
    }

    public void UpdateState(PlaybackState state)
    {
        if (_smtc == null)
        {
            return;
        }

        var newStatus = state switch
        {
            PlaybackState.Playing => MediaPlaybackStatus.Playing,
            PlaybackState.Paused => MediaPlaybackStatus.Paused,
            _ => MediaPlaybackStatus.Stopped,
        };

        var oldStatus = _smtc.PlaybackStatus;
        if (oldStatus != newStatus)
        {
            _logger.LogInformation(
                "SMTC PlaybackStatus: {Old} -> {New} (PlaybackState={State})",
                oldStatus,
                newStatus,
                state);
            _smtc.PlaybackStatus = newStatus;
        }
    }

    public void UpdateSupportedCommands(IReadOnlyCollection<string>? supportedCommands)
    {
        _supportedCommands = supportedCommands;
        if (_smtc != null)
        {
            ApplySupportedCommands(_smtc);
        }
    }

    public void UpdateMetadata(TrackMetadata? track)
    {
        if (_displayUpdater == null)
        {
            return;
        }

        if (track == null)
        {
            _displayUpdater.ClearAll();
            _displayUpdater.Type = MediaPlaybackType.Music;
        }
        else
        {
            var music = _displayUpdater.MusicProperties;
            music.Title = track.Title ?? string.Empty;
            music.Artist = track.Artist ?? string.Empty;
            music.AlbumTitle = track.Album ?? string.Empty;
        }

        _displayUpdater.Update();
    }

    public void UpdateThumbnail(byte[]? imageBytes)
    {
        if (_displayUpdater == null)
        {
            return;
        }

        try
        {
            _displayUpdater.Thumbnail = imageBytes is { Length: > 0 }
                ? CreateStreamReference(imageBytes)
                : null;
            _displayUpdater.Update();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update SMTC thumbnail");
        }
    }

    public void UpdateTimeline(TimeSpan position, TimeSpan duration)
    {
        if (_smtc == null || _timeline == null)
        {
            return;
        }

        _timeline.Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        _timeline.EndTime = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;

        try
        {
            _smtc.UpdateTimelineProperties(_timeline);
        }
        catch (Exception ex)
        {
            // The OS refused the update. This should not happen on the supported
            // minimum build; stop retrying on every position tick rather than
            // logging once per tick.
            _logger.LogWarning(ex, "SMTC timeline update failed; timeline updates disabled");
            _timeline = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_smtc != null)
        {
            _smtc.ButtonPressed -= OnButtonPressed;
            _smtc.IsEnabled = false;
            _smtc = null;
            _displayUpdater = null;
            _timeline = null;
        }
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        _logger.LogInformation(
            "SMTC button pressed: {Button} (current SMTC PlaybackStatus={Status}, IsEnabled={Enabled})",
            args.Button,
            sender.PlaybackStatus,
            _isEnabled);

        if (!_isEnabled)
        {
            return;
        }

        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
            case SystemMediaTransportControlsButton.Pause:
                PlayPauseRequested?.Invoke(this, EventArgs.Empty);
                break;
            case SystemMediaTransportControlsButton.Next:
                NextRequested?.Invoke(this, EventArgs.Empty);
                break;
            case SystemMediaTransportControlsButton.Previous:
                PreviousRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void ApplySupportedCommands(SystemMediaTransportControls smtc)
    {
        smtc.IsPlayEnabled = TransportCommands.IsSupported(_supportedCommands, Commands.Play);
        smtc.IsPauseEnabled = TransportCommands.IsSupported(_supportedCommands, Commands.Pause);
        smtc.IsNextEnabled = TransportCommands.IsSupported(_supportedCommands, Commands.Next);
        smtc.IsPreviousEnabled = TransportCommands.IsSupported(_supportedCommands, Commands.Previous);
    }

    private static RandomAccessStreamReference CreateStreamReference(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        try
        {
            writer.WriteBytes(bytes);
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            writer.DetachStream();
        }
        finally
        {
            writer.Dispose();
        }

        stream.Seek(0);
        return RandomAccessStreamReference.CreateFromStream(stream);
    }
}
