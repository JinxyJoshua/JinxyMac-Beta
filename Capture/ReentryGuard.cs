namespace JinxyMac.Capture;

/// <summary>
/// A synchronous "only one caller at a time" gate.
/// </summary>
/// <remarks>
/// Built for exactly one shape of bug: a start method that checks "is this
/// already running" before an <c>await</c> (or before any operation slow
/// enough to matter — spawning ffmpeg, deleting stale files) and only assigns
/// the field that check reads afterwards. A second call arriving in that gap
/// reads the same "not running" state the first call did, and both proceed.
///
/// <see cref="TryEnter"/> is a single interlocked compare-and-swap, so it can
/// be called synchronously as the very first line of the guarded method —
/// before any <c>await</c>, before any I/O — which is what makes a second call
/// arriving mid-start fail immediately instead of racing the first to
/// completion. <see cref="Exit"/> must run in a <c>finally</c> so the gate
/// opens again however the guarded method leaves, success or exception.
/// </remarks>
internal sealed class ReentryGuard
{
    private int _state;

    /// <returns>True if this call now holds the gate. False if another call
    /// already holds it, in which case this call must not proceed.</returns>
    public bool TryEnter() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

    /// <summary>Releases the gate. Safe to call even if it was never entered.</summary>
    public void Exit() => Interlocked.Exchange(ref _state, 0);
}
