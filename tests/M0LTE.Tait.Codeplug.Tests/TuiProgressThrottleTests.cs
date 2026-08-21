using AwesomeAssertions;
using M0LTE.Tait.Codeplug.Cli;
using Xunit;

namespace M0LTE.Tait.Codeplug.Tests;

/// <summary>
/// A write reports progress once per record, and a full codeplug is over a thousand records. Since
/// Terminal.Gui repaints the whole screen for any change, redrawing on every one of those would push
/// a thousand full repaints down what is often an SSH link. These pin the rules that stop that: only
/// on a change of whole percent, no more often than the interval, and always at the end.
/// </summary>
public class TuiProgressThrottleTests
{
    private static readonly DateTime T0 = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_first_update_is_drawn()
    {
        var throttle = new TuiProgressThrottle(TimeSpan.FromMilliseconds(250));

        throttle.ShouldDraw(0.0, isFinal: false, T0).Should().BeTrue();
    }

    [Fact]
    public void Updates_within_the_same_percent_are_skipped()
    {
        var throttle = new TuiProgressThrottle(TimeSpan.FromMilliseconds(250));
        throttle.ShouldDraw(0.10, isFinal: false, T0).Should().BeTrue();

        // 1103 records at 10.0x% - the same whole percent, a second later.
        throttle.ShouldDraw(0.1004, isFinal: false, T0.AddSeconds(1)).Should().BeFalse();
        throttle.ShouldDraw(0.1009, isFinal: false, T0.AddSeconds(2)).Should().BeFalse();
    }

    [Fact]
    public void A_new_percent_too_soon_is_skipped()
    {
        var throttle = new TuiProgressThrottle(TimeSpan.FromMilliseconds(250));
        throttle.ShouldDraw(0.10, isFinal: false, T0).Should().BeTrue();

        throttle.ShouldDraw(0.11, isFinal: false, T0.AddMilliseconds(50)).Should().BeFalse();
        throttle.ShouldDraw(0.12, isFinal: false, T0.AddMilliseconds(300)).Should().BeTrue();
    }

    [Fact]
    public void The_last_update_is_always_drawn()
    {
        var throttle = new TuiProgressThrottle(TimeSpan.FromMilliseconds(250));
        throttle.ShouldDraw(0.98, isFinal: false, T0).Should().BeTrue();

        // Immediately after, and the same percent: still drawn, or the bar stops short of the end.
        throttle.ShouldDraw(0.98, isFinal: true, T0.AddMilliseconds(1)).Should().BeTrue();
    }

    [Fact]
    public void A_thousand_record_write_costs_a_handful_of_redraws()
    {
        var throttle = new TuiProgressThrottle(TimeSpan.FromMilliseconds(250));
        const int Records = 1103;

        // A record every 20ms, which is about what the radio manages at 19200 baud.
        int drawn = 0;
        for (int i = 1; i <= Records; i++)
        {
            bool last = i == Records;
            if (throttle.ShouldDraw((double)i / Records, last, T0.AddMilliseconds(i * 20)))
            {
                drawn++;
            }
        }

        drawn.Should().BeLessThan(100, "the point is to redraw a few times a second, not 1103 times");
        drawn.Should().BeGreaterThan(10, "but it still has to look like it is moving");
    }
}
