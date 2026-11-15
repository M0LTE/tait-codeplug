using System.Text;
using AwesomeAssertions;
using M0LTE.Tait.Codeplug;
using M0LTE.Tait.Codeplug.Cli;
using Xunit;

namespace M0LTE.Tait.Codeplug.Tests;

/// <summary><c>write</c> only sends a saved codeplug to the radio it was read from, on the database
/// version the radio still has (issue #20).</summary>
public class WriteGuardTests
{
    private static CodeplugImage Image(string? serial, string firmware = "QMA1F_std_02.19.00.03",
        string versions = "0095,0086", string dbRecord = "5F00")
    {
        var records = new List<CodeplugRecord>
        {
            new(0x00, 0, Encoding.ASCII.GetBytes("TMAB12-B100_0201")),
            new(0x00, 1, Encoding.ASCII.GetBytes(firmware)),
            new(0x00, 2, Encoding.ASCII.GetBytes(versions)),
            new(0x27, 0, Convert.FromHexString(dbRecord)),
        };
        if (serial is not null)
        {
            records.Add(new CodeplugRecord(0x00, 5, Encoding.ASCII.GetBytes(serial)));
        }

        return new CodeplugImage([], records);
    }

    [Fact]
    public void The_same_radio_on_the_same_version_is_allowed() =>
        WriteGuard.Refusal(Image("10000001"), Image("10000001")).Should().BeNull();

    [Fact]
    public void Another_radio_is_refused_and_both_serials_are_named() =>
        WriteGuard.Refusal(Image("10000001"), Image("10000002")).Should()
            .Contain("10000001").And.Contain("10000002");

    [Fact]
    public void A_file_with_no_serial_is_refused_before_any_radio_is_involved()
    {
        WriteGuard.FileRefusal(Image(null)).Should().NotBeNull();
        WriteGuard.Refusal(Image("10000001"), Image(null)).Should().NotBeNull();
    }

    [Fact]
    public void A_radio_that_reports_no_serial_is_refused() =>
        WriteGuard.Refusal(Image(null), Image("10000001")).Should().NotBeNull();

    [Fact]
    public void A_file_from_before_a_reflash_is_refused()
    {
        CodeplugImage file = Image("10000001", "QMA1F_std_02.18.00.00", "0094,0086", "5E00");
        CodeplugImage radio = Image("10000001");

        WriteGuard.Refusal(radio, file).Should().Contain("0094,0086").And.Contain("0095,0086");
    }

    [Fact]
    public void A_mismatched_database_version_record_is_refused_even_if_the_strings_agree() =>
        WriteGuard.Refusal(Image("10000001"), Image("10000001", dbRecord: "5E00")).Should().Contain("0x27");
}
