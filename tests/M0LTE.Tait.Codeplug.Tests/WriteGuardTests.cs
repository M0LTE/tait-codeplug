using AwesomeAssertions;
using M0LTE.Tait.Codeplug;
using M0LTE.Tait.Codeplug.Cli;
using Xunit;

namespace M0LTE.Tait.Codeplug.Tests;

/// <summary><c>write</c> only sends a saved codeplug to the radio it was read from (issue #20).</summary>
public class WriteGuardTests
{
    private static TaitIdentity Identity(string? serial)
    {
        var records = new List<CodeplugRecord>
        {
            new(0x00, 0, System.Text.Encoding.ASCII.GetBytes("TMAB12-B100_0201")),
            new(0x00, 1, System.Text.Encoding.ASCII.GetBytes("QMA1F_std_02.19.00.03")),
            new(0x00, 2, System.Text.Encoding.ASCII.GetBytes("0095,0086")),
        };
        if (serial is not null)
        {
            records.Add(new CodeplugRecord(0x00, 5, System.Text.Encoding.ASCII.GetBytes(serial)));
        }

        return TaitIdentity.FromSectionZero(records);
    }

    [Fact]
    public void The_same_radio_is_allowed() =>
        WriteGuard.Refusal(Identity("10000001"), Identity("10000001")).Should().BeNull();

    [Fact]
    public void Another_radio_is_refused_and_both_serials_are_named() =>
        WriteGuard.Refusal(Identity("10000001"), Identity("10000002")).Should()
            .Contain("10000001").And.Contain("10000002");

    [Fact]
    public void A_file_with_no_serial_is_refused() =>
        WriteGuard.Refusal(Identity("10000001"), Identity(null)).Should().NotBeNull();

    [Fact]
    public void A_radio_that_reports_no_serial_is_refused() =>
        WriteGuard.Refusal(Identity(null), Identity("10000001")).Should().NotBeNull();
}
