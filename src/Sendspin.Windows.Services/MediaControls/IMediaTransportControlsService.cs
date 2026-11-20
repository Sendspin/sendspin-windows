using Sendspin.SDK.Models;

namespace Sendspin.Windows.Services.MediaControls;

public interface IMediaTransportControlsService : IDisposable
{
    /// <summary>
    /// Gets or sets whether System Media Transport Controls integration is active.
    /// When false, Windows hides our media controls and incoming button events are ignored.
    /// </summary>
    bool IsEnabled { get; set; }

    event EventHandler? PlayPauseRequested;

    event EventHandler? NextRequested;

    event EventHandler? PreviousRequested;

    void Initialize(IntPtr windowHandle);

    void UpdateState(PlaybackState state);

    /// <summary>
    /// Enables only the media keys whose command the active group lists in its
    /// <c>supported_commands</c>; null (not connected, or no list yet) disables them all.
    /// </summary>
    void UpdateSupportedCommands(IReadOnlyCollection<string>? supportedCommands);

    void UpdateMetadata(TrackMetadata? track);

    void UpdateThumbnail(byte[]? imageBytes);

    /// <summary>
    /// Publishes the current playback position and track duration to SMTC so the
    /// Windows media overlay (and third-party SMTC readers) can show progress.
    /// Call with <see cref="TimeSpan.Zero"/> for both values to clear the timeline
    /// (no track, or the position is unknown). A duration of
    /// <see cref="TimeSpan.Zero"/> marks an unbounded stream (live radio); the
    /// elapsed position is still shown. No seek range is reported: a client
    /// cannot seek on its own (only the group's controller can), and seek
    /// requests from the media overlay are not handled.
    /// </summary>
    void UpdateTimeline(TimeSpan position, TimeSpan duration);
}
