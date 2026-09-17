using M0LTE.Tait.Codeplug.Cli;

namespace M0LTE.Tait.Codeplug.Tests;

/// <summary>
/// A failure in interactive mode used to be invisible: the screen owns the terminal, so an exception
/// that reached the top took the application down and the runtime printed its trace into a terminal
/// that had just been handed back, where it was wiped. The report below is what replaces that, and
/// what someone pastes into an issue, so the things worth pinning are that it says what the tool was
/// doing, and that trying to save it can never itself throw - it only ever runs when something has
/// already gone wrong.
/// </summary>
public class CrashReportTests
{
    private static readonly DateTimeOffset When = new(2026, 9, 17, 20, 15, 30, TimeSpan.Zero);

    [Fact]
    public void The_report_says_what_the_tool_was_doing_when_it_failed()
    {
        var boom = new InvalidOperationException("the radio stopped answering");

        string report = CrashReport.Format("talking to the radio", boom, When);

        report.Should().Contain("talking to the radio", "a stack trace alone does not say what was being attempted");
        report.Should().Contain("the radio stopped answering");
        report.Should().Contain(nameof(InvalidOperationException), "the type says what kind of fault it was");
        report.Should().Contain("2026-09-17", "an intermittent fault is reported later, so it needs its own timestamp");
    }

    [Fact]
    public void The_report_carries_the_inner_exception_too()
    {
        // The interesting half of a wrapped failure is almost always the inner one.
        var inner = new TimeoutException("no programming response within the transaction deadline");
        var outer = new InvalidOperationException("read failed", inner);

        CrashReport.Format("reading the radio", outer, When)
            .Should().Contain("no programming response within the transaction deadline");
    }

    [Fact]
    public void A_report_with_no_stack_trace_is_still_a_report()
    {
        // An exception that was constructed but never thrown has no stack. Formatting must not depend
        // on there being one, because a report that throws while reporting leaves us where we started.
        Action act = () => CrashReport.Format("starting up", new InvalidOperationException("never thrown"), When);

        act.Should().NotThrow();
    }

    [Fact]
    public void File_names_are_timestamped_so_repeated_failures_do_not_overwrite_each_other()
    {
        // The fault that prompted this was intermittent: a session can fail more than once, and the
        // run that finally reproduces it must not erase the one before.
        string first = CrashReport.FileName(When);
        string second = CrashReport.FileName(When.AddMilliseconds(4));

        first.Should().NotBe(second);
        first.Should().StartWith("tait-codeplug-crash-").And.EndWith(".log");
        first.Should().Contain("20260917");
    }

    [Fact]
    public void File_names_sort_oldest_first()
    {
        // Whoever is looking wants the last one, so the names have to order the way the clock does.
        string[] names =
        [
            CrashReport.FileName(When.AddHours(2)),
            CrashReport.FileName(When),
            CrashReport.FileName(When.AddMinutes(1)),
        ];

        names.Order(StringComparer.Ordinal).Should().Equal(
            CrashReport.FileName(When),
            CrashReport.FileName(When.AddMinutes(1)),
            CrashReport.FileName(When.AddHours(2)));
    }

    [Fact]
    public void Writing_a_report_puts_it_somewhere_readable_and_returns_where()
    {
        string? path = CrashReport.Write("a report", When);

        path.Should().NotBeNull();
        try
        {
            File.ReadAllText(path!).Should().Be("a report");
        }
        finally
        {
            File.Delete(path!);
        }
    }

    [Fact]
    public void Failing_to_write_the_report_is_not_a_second_failure()
    {
        // Reporting runs when things are already bad, and a read-only or full disk is exactly when a
        // tool is most likely to be failing. Returning null lets the caller fall back to stderr.
        Func<string?> act = () => CrashReport.Write(null!, When);

        act.Should().NotThrow();
    }
}
