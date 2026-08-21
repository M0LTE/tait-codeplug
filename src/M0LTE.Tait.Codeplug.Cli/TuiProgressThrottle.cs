namespace M0LTE.Tait.Codeplug.Cli;

/// <summary>
/// Decides whether a progress update is worth putting on screen.
///
/// A write is over a thousand records and reports one update each, so redrawing on every one would be
/// a thousand redraws. That matters more than it sounds: Terminal.Gui repaints the whole screen for
/// any change, ~22 KB of escape sequences at 100x30, so an unthrottled progress bar would push a
/// thousand full repaints down what may well be an SSH link, and the progress bar itself would become
/// the slowest thing in the write.
///
/// So an update is drawn only when the percentage actually changes, and at most a few times a second.
/// The final update is always drawn, so the bar never stops short of the end.
/// </summary>
internal sealed class TuiProgressThrottle(TimeSpan minimumInterval)
{
    private int _lastPercent = -1;
    private DateTime _lastDrawUtc = DateTime.MinValue;

    /// <summary>Four a second: fast enough to look live, slow enough to stay out of the way.</summary>
    internal TuiProgressThrottle()
        : this(TimeSpan.FromMilliseconds(250))
    {
    }

    /// <summary>
    /// Whether to redraw for this update. <paramref name="nowUtc"/> is passed in rather than read from
    /// the clock so this is testable.
    /// </summary>
    internal bool ShouldDraw(double? fraction, bool isFinal, DateTime nowUtc)
    {
        if (isFinal)
        {
            _lastPercent = fraction is null ? -1 : (int)(fraction.Value * 100);
            _lastDrawUtc = nowUtc;
            return true;
        }

        int percent = fraction is null ? -1 : (int)(fraction.Value * 100);
        if (percent == _lastPercent)
        {
            return false;
        }

        if (nowUtc - _lastDrawUtc < minimumInterval)
        {
            return false;
        }

        _lastPercent = percent;
        _lastDrawUtc = nowUtc;
        return true;
    }
}
