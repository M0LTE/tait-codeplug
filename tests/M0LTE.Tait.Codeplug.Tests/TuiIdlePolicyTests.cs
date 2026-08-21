using M0LTE.Tait.Codeplug.Cli;

namespace M0LTE.Tait.Codeplug.Tests;

/// <summary>
/// The interactive mode steps its main loop down when it is left alone, so that a tool sitting there
/// doing nothing stops writing to the terminal 25 times a second. The rates themselves are measured
/// (see <see cref="TuiIdlePolicy"/>); what is worth pinning down here is the shape of the decision,
/// because getting it wrong is either a tool that feels sluggish to type into or one that never goes
/// quiet.
/// </summary>
public class TuiIdlePolicyTests
{
    private const ushort LibraryRate = 25;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(9.999)]
    public void Typing_runs_at_the_librarys_own_rate(double secondsSinceInput)
    {
        TuiIdlePolicy.RateFor(TimeSpan.FromSeconds(secondsSinceInput), LibraryRate)
            .Should().Be(LibraryRate, "a pause shorter than the threshold must not slow typing down");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(59.999)]
    public void A_pause_steps_down_to_a_rate_that_still_reads_as_instant(double secondsSinceInput)
    {
        TuiIdlePolicy.RateFor(TimeSpan.FromSeconds(secondsSinceInput), LibraryRate)
            .Should().Be(TuiIdlePolicy.PausedIterationsPerSecond);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(3600)]
    public void Walking_away_steps_down_again(double secondsSinceInput)
    {
        TuiIdlePolicy.RateFor(TimeSpan.FromSeconds(secondsSinceInput), LibraryRate)
            .Should().Be(TuiIdlePolicy.AwayIterationsPerSecond);
    }

    [Fact]
    public void The_step_down_is_monotonic()
    {
        ushort previous = LibraryRate;

        for (double s = 0; s <= 120; s += 0.5)
        {
            ushort rate = TuiIdlePolicy.RateFor(TimeSpan.FromSeconds(s), LibraryRate);
            rate.Should().BeLessThanOrEqualTo(previous, $"the rate must never rise again at {s}s idle");
            previous = rate;
        }
    }

    [Fact]
    public void It_never_speeds_the_loop_up_beyond_what_the_library_asked_for()
    {
        // A library (or a future version of it) that already ticks slowly than our idle rates must be
        // left alone: stepping "down" to 10/s would be stepping up.
        const ushort SlowLibrary = 2;

        TuiIdlePolicy.RateFor(TimeSpan.FromSeconds(0), SlowLibrary).Should().Be(SlowLibrary);
        TuiIdlePolicy.RateFor(TimeSpan.FromSeconds(30), SlowLibrary).Should().Be(SlowLibrary);
        TuiIdlePolicy.RateFor(TimeSpan.FromSeconds(300), SlowLibrary).Should().Be(SlowLibrary);
    }
}
