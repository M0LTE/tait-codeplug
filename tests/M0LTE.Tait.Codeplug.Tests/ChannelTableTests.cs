using M0LTE.Tait.Codeplug;

namespace M0LTE.Tait.Codeplug.Tests;

/// <summary>
/// Adding and removing channels changes the codeplug's shape, not just field values: the channel table
/// (0x05) grows or shrinks by 181 bits, and the CIB channel index (0x07) by a 15-bit entry.
///
/// The expected bytes below are not invented. They are what real CPS saves and radio readouts carry:
/// every multi-channel codeplug to hand (a 2-channel radio readout, a 6-channel CPS save) has CIB
/// block 0 with Rec = ID = the channel index, and the CPS chunks a table into 32-byte records with the
/// remainder last. These tests pin our output to that.
/// </summary>
public class ChannelTableTests
{
    // Observed in TM8100 - 19925328.m8p, readout.m8p and verify.m8p, all 2-channel.
    private const string RealTwoChannelCib = "00008100";

    // Observed in ctcss-rx-values.m8p, a 6-channel CPS save.
    private const string RealSixChannelCib = "0000810081c0608040502800";

    private static CodeplugFields OneChannel()
    {
        // 23 bytes = one 181-bit channel rounded up, which is exactly what a 1-channel CPS save carries.
        var image = new CodeplugImage(
            [new KeyValuePair<string, string>("DBVer", "0095")],
            [new CodeplugRecord(0x05, 0, new byte[23]), new CodeplugRecord(0x07, 0, new byte[2])]);
        return CodeplugFields.Open(image);
    }

    [Fact]
    public void Adding_a_channel_grows_the_table_and_writes_the_cib_entry()
    {
        CodeplugFields fields = OneChannel();

        int added = fields.AddChannel();

        added.Should().Be(1);
        fields.ChannelCount.Should().Be(2);
        fields.Image.SectionBytes(0x05).Should().HaveCount(46);
        Convert.ToHexString(fields.Image.SectionBytes(0x07)).ToLowerInvariant().Should().Be(RealTwoChannelCib);
    }

    [Fact]
    public void A_six_channel_table_matches_a_real_six_channel_codeplug()
    {
        CodeplugFields fields = OneChannel();

        for (int i = 0; i < 5; i++)
        {
            fields.AddChannel();
        }

        fields.ChannelCount.Should().Be(6);
        fields.Image.SectionBytes(0x05).Should().HaveCount(136);
        Convert.ToHexString(fields.Image.SectionBytes(0x07)).ToLowerInvariant().Should().Be(RealSixChannelCib);
    }

    [Fact]
    public void Tables_are_chunked_into_records_the_way_the_cps_chunks_them()
    {
        CodeplugFields fields = OneChannel();
        fields.AddChannel();

        // A real 2-channel save is 32 + 14; a 6-channel one is 32 + 32 + 32 + 32 + 8.
        fields.Image.Records.Where(r => r.Section == 0x05).OrderBy(r => r.Index)
            .Select(r => r.Data.Length).Should().Equal(32, 14);

        for (int i = 0; i < 4; i++)
        {
            fields.AddChannel();
        }

        fields.Image.Records.Where(r => r.Section == 0x05).OrderBy(r => r.Index)
            .Select(r => r.Data.Length).Should().Equal(32, 32, 32, 32, 8);
    }

    [Fact]
    public void A_new_channel_starts_as_a_copy_of_the_one_before_it()
    {
        CodeplugFields fields = OneChannel();
        fields.SetRxFrequencyHz(0, 144_812_500);
        fields.SetTxFrequencyHz(0, 144_812_500);
        fields.SetBandwidth(0, Bandwidth.Wide);
        fields.SetPowerLevel(0, PowerLevel.Medium);

        int added = fields.AddChannel();

        fields.GetRxFrequencyHz(added).Should().Be(144_812_500);
        fields.GetTxFrequencyHz(added).Should().Be(144_812_500);
        fields.GetBandwidth(added).Should().Be(Bandwidth.Wide);
        fields.GetPowerLevel(added).Should().Be(PowerLevel.Medium);
    }

    [Fact]
    public void Editing_a_new_channel_leaves_the_others_alone()
    {
        CodeplugFields fields = OneChannel();
        fields.SetRxFrequencyHz(0, 144_800_000);
        fields.AddChannel();

        fields.SetRxFrequencyHz(1, 430_925_000);
        fields.SetPowerLevel(1, PowerLevel.Low);

        fields.GetRxFrequencyHz(0).Should().Be(144_800_000);
        fields.GetRxFrequencyHz(1).Should().Be(430_925_000);
    }

    [Fact]
    public void Removing_a_channel_shifts_the_ones_above_it_down()
    {
        CodeplugFields fields = OneChannel();
        fields.AddChannel();
        fields.AddChannel();
        fields.SetRxFrequencyHz(0, 144_800_000);
        fields.SetRxFrequencyHz(1, 144_812_500);
        fields.SetRxFrequencyHz(2, 430_925_000);

        fields.RemoveChannel(1);

        fields.ChannelCount.Should().Be(2);
        fields.GetRxFrequencyHz(0).Should().Be(144_800_000);
        fields.GetRxFrequencyHz(1).Should().Be(430_925_000);
        Convert.ToHexString(fields.Image.SectionBytes(0x07)).ToLowerInvariant().Should().Be(RealTwoChannelCib);
    }

    [Fact]
    public void Removing_the_only_channel_is_refused()
    {
        CodeplugFields fields = OneChannel();

        Action act = () => fields.RemoveChannel(0);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void An_out_of_range_channel_is_refused()
    {
        CodeplugFields fields = OneChannel();
        fields.AddChannel();

        Action act = () => fields.RemoveChannel(2);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_resized_codeplug_still_round_trips_through_m8p()
    {
        CodeplugFields fields = OneChannel();
        fields.AddChannel();
        fields.SetRxFrequencyHz(1, 430_925_000);

        // LoadM8p verifies every record checksum, so a bad rebuild fails here.
        CodeplugImage reloaded = CodeplugImage.LoadM8p(fields.Image.ToM8p());

        CodeplugFields reopened = CodeplugFields.Open(reloaded);
        reopened.ChannelCount.Should().Be(2);
        reopened.GetRxFrequencyHz(1).Should().Be(430_925_000);
        Convert.ToHexString(reloaded.SectionBytes(0x07)).ToLowerInvariant().Should().Be(RealTwoChannelCib);
    }
}
