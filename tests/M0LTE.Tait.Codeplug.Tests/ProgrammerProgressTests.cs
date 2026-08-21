using AwesomeAssertions;
using M0LTE.Tait.Codeplug;
using Xunit;

namespace M0LTE.Tait.Codeplug.Tests;

/// <summary>
/// A read is ~25 seconds and a write is comparable, both behind a connect that can sit for 90 seconds
/// waiting for the operator to power-cycle the radio. The caller needs two things from that: to know
/// when the radio has actually latched, so a "power-cycle now" prompt can take itself down, and to
/// know how far along the transfer is. These pin down both, plus the cancellation the prompt's Cancel
/// button relies on.
/// </summary>
public class ProgrammerProgressTests
{
    private static Dictionary<string, string> ReadScript() => new()
    {
        ["^"] = "v",
        ["#"] = ">",
        ["ld"] = "{C05}\r>",
        ["d00"] = "{C01}\r>",
        ["p00"] = ">",
        ["p01"] = ">",

        // The real captured section 0, so the .m8p header decode has an identity to work from.
        ["r00"] =
            "000010544D414231322D423130305F3032303147\r" +
            "000115514D4131465F7374645F30322E31382E30302E303076\r" +
            "000209303039342C303038362E\r" +
            "0003040000FFAA50\r" +
            "000405544D4143359D\r" +
            "00050831393932353332384C\r" +
            "00060830313032303030306F\r>",
    };

    /// <summary>One valid record, built with the library's own encoder so the checksum is real.</summary>
    private static string OneRecord(byte section)
        => new CodeplugRecord(section, 0, [0x01]).ToWireLine() + "\r>";

    /// <summary>Answers any section read with one record, so a read walks every section.</summary>
    private static string? EmptySection(string command)
        => command.StartsWith('r') && command.Length == 3
            ? OneRecord(Convert.ToByte(command[1..], 16))
            : null;

    [Fact]
    public void Connecting_reports_waiting_then_latched()
    {
        var radio = new ScriptedRadio(ReadScript(), EmptySection);
        using var programmer = new TaitProgrammer(radio);
        var phases = new List<ProgrammerPhase>();
        programmer.Progress += (_, p) => phases.Add(p.Phase);

        programmer.Connect();

        phases.Should().StartWith([ProgrammerPhase.WaitingForRadio, ProgrammerPhase.Connected],
            "the prompt goes up on the first and comes down on the second");
    }

    [Fact]
    public void A_read_reports_progress_that_reaches_the_end()
    {
        var radio = new ScriptedRadio(ReadScript(), EmptySection);
        using var programmer = new TaitProgrammer(radio);
        var reads = new List<ProgrammerProgress>();
        programmer.Progress += (_, p) =>
        {
            if (p.Phase == ProgrammerPhase.Reading)
            {
                reads.Add(p);
            }
        };

        programmer.ReadImage();

        reads.Should().NotBeEmpty();
        reads[0].Fraction.Should().Be(0);
        reads[^1].Fraction.Should().Be(1, "a bar that stops at 96% looks like a hang");
        reads.Select(r => r.Done).Should().BeInAscendingOrder();
    }

    [Fact]
    public void A_write_reports_one_step_per_record_and_then_commits()
    {
        var radio = new ScriptedRadio(WriteScript(), WriteFallback);
        using var programmer = new TaitProgrammer(radio, AllowWrite());
        var progress = new List<ProgrammerProgress>();
        programmer.Progress += (_, p) => progress.Add(p);

        int written = programmer.WriteRecords(TwoRecords());

        written.Should().Be(2);
        progress.Where(p => p.Phase == ProgrammerPhase.Writing).Select(p => p.Done)
            .Should().Equal([1, 2]);
        progress.Where(p => p.Phase == ProgrammerPhase.Writing).Select(p => p.Total)
            .Should().AllBeEquivalentTo(2);
        progress[^1].Phase.Should().Be(ProgrammerPhase.Committed);
    }

    [Fact]
    public void Cancelling_while_waiting_for_the_radio_throws_and_writes_nothing()
    {
        // A radio that never answers the reset probe: the case the Cancel button exists for.
        var radio = new ScriptedRadio(new Dictionary<string, string>(), _ => string.Empty);
        using var programmer = new TaitProgrammer(radio, new ProgrammerOptions { ConnectWaitMs = 10_000 });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Action connect = () => programmer.Connect(cts.Token);

        connect.Should().Throw<OperationCanceledException>();
        radio.CommandsSeen.Should().BeEmpty("cancelling before the first probe must not touch the radio");
    }

    [Fact]
    public void Cancelling_a_write_before_the_block_opens_leaves_the_radio_alone()
    {
        var radio = new ScriptedRadio(WriteScript(), WriteFallback);
        using var programmer = new TaitProgrammer(radio, AllowWrite());
        using var cts = new CancellationTokenSource();

        // Cancel once the preamble has run, which is the last point at which stopping is safe.
        programmer.Progress += (_, p) =>
        {
            if (p.Phase == ProgrammerPhase.PreparingWrite)
            {
                cts.Cancel();
            }
        };

        Action write = () => programmer.WriteRecords(TwoRecords(), cts.Token);

        write.Should().Throw<OperationCanceledException>();
        radio.CommandsSeen.Should().NotContain("b", "the write block must never be left open");
        radio.CommandsSeen.Should().NotContain(c => c.StartsWith('w'));
    }

    [Fact]
    public void A_write_that_has_started_is_not_abandoned_half_way()
    {
        var radio = new ScriptedRadio(WriteScript(), WriteFallback);
        using var programmer = new TaitProgrammer(radio, AllowWrite());
        using var cts = new CancellationTokenSource();

        // Cancel after the first record has gone out. Stopping there would leave the block open and
        // the codeplug partly written, so the write is expected to run to its commit regardless.
        programmer.Progress += (_, p) =>
        {
            if (p is { Phase: ProgrammerPhase.Writing, Done: 1 })
            {
                cts.Cancel();
            }
        };

        int written = programmer.WriteRecords(TwoRecords(), cts.Token);

        written.Should().Be(2);
        radio.CommandsSeen.Should().Contain("e", "an opened write block is always committed");
    }

    private static ProgrammerOptions AllowWrite() => new() { AllowUnvalidatedWrite = true };

    private static List<CodeplugRecord> TwoRecords() =>
    [
        new(0x05, 0, [0x01]),
        new(0x05, 1, [0x02]),
    ];

    private static Dictionary<string, string> WriteScript() => new()
    {
        ["^"] = "v",
        ["#"] = ">",
        ["ld"] = "{C05}\r>",
        ["d00"] = "{C01}\r>",
        ["p00"] = ">",
        ["p01"] = ">",
        ["b"] = ">",
        ["e"] = ">",
    };

    private static string? WriteFallback(string command)
    {
        if (command.StartsWith('r') && command.Length == 3)
        {
            return OneRecord(Convert.ToByte(command[1..], 16));
        }

        return command.StartsWith('w') || command.StartsWith('i') ? ">" : null;
    }
}
