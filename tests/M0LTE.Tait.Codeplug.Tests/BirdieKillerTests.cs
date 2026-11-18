using AwesomeAssertions;
using M0LTE.Tait.Codeplug;
using Xunit;

namespace M0LTE.Tait.Codeplug.Tests;

/// <summary>The per-channel "Activate Birdie Killer" option, pinned against two CPS saves that differ
/// in that tick box on the first channel and nothing else (see <see cref="BirdieKillerFixtures"/>).</summary>
public class BirdieKillerTests
{
    private static CodeplugImage Load(string m8p) => CodeplugImage.LoadM8p(m8p);

    /// <summary>Every line of an .m8p except the Date header, which the CPS stamps on each save.</summary>
    private static string WithoutDate(string m8p) =>
        string.Join("\r\n", m8p.Split("\r\n").Where(l => !l.StartsWith("Date=", StringComparison.Ordinal)));

    [Fact]
    public void The_fixtures_round_trip_through_load_and_save_unchanged()
    {
        Load(BirdieKillerFixtures.ThirdChannelOn).ToM8p().Should().Be(BirdieKillerFixtures.ThirdChannelOn);
        Load(BirdieKillerFixtures.FirstAndThirdChannelsOn).ToM8p().Should().Be(BirdieKillerFixtures.FirstAndThirdChannelsOn);
    }

    [Fact]
    public void Reads_off_off_on_and_on_off_on_from_the_two_cps_saves()
    {
        CodeplugFields third = CodeplugFields.Open(Load(BirdieKillerFixtures.ThirdChannelOn));
        third.ChannelCount.Should().Be(3);
        Enumerable.Range(0, 3).Select(third.GetBirdieKiller).Should().Equal(false, false, true);

        CodeplugFields both = CodeplugFields.Open(Load(BirdieKillerFixtures.FirstAndThirdChannelsOn));
        both.ChannelCount.Should().Be(3);
        Enumerable.Range(0, 3).Select(both.GetBirdieKiller).Should().Equal(true, false, true);
    }

    [Fact]
    public void Ticking_the_first_channel_reproduces_the_second_save_byte_for_byte()
    {
        CodeplugImage image = Load(BirdieKillerFixtures.ThirdChannelOn);
        FieldConsole.Set(CodeplugFields.Open(image), "ch0.birdiekiller", "true");

        string saved = image.ToM8p();
        saved.Should().NotBe(BirdieKillerFixtures.FirstAndThirdChannelsOn, "the Date header still differs");
        WithoutDate(saved).Should().Be(WithoutDate(BirdieKillerFixtures.FirstAndThirdChannelsOn));
    }

    [Fact]
    public void Unticking_the_first_channel_reproduces_the_first_save_byte_for_byte()
    {
        CodeplugImage image = Load(BirdieKillerFixtures.FirstAndThirdChannelsOn);
        FieldConsole.Set(CodeplugFields.Open(image), "ch0.birdiekiller", "false");

        WithoutDate(image.ToM8p()).Should().Be(WithoutDate(BirdieKillerFixtures.ThirdChannelOn));
    }

    [Fact]
    public void Setting_it_changes_the_first_two_bits_of_that_channel_and_nothing_else()
    {
        CodeplugImage before = Load(BirdieKillerFixtures.ThirdChannelOn);
        CodeplugImage after = Load(BirdieKillerFixtures.ThirdChannelOn);
        CodeplugFields.Open(after).SetBirdieKiller(0, true);

        after.Header.Should().Equal(before.Header);
        after.Records.Should().HaveCount(before.Records.Count);
        for (int i = 0; i < before.Records.Count; i++)
        {
            CodeplugRecord was = before.Records[i];
            CodeplugRecord now = after.Records[i];
            (now.Section, now.Index).Should().Be((was.Section, was.Index));
            if (was.Section == 0x05 && was.Index == 0)
            {
                // Channel 0 starts at bit 0 of the table: bits 0 and 1 of the first byte, and only those.
                now.Data[0].Should().Be((byte)(was.Data[0] | 0x03));
                now.Data.AsSpan(1).SequenceEqual(was.Data.AsSpan(1)).Should().BeTrue();
            }
            else
            {
                now.Data.Should().Equal(was.Data, $"record 0x{was.Section:X2}/{was.Index} is not the birdie killer's");
            }
        }
    }

    [Fact]
    public void Each_channel_has_its_own_pair_at_the_start_of_its_181_bit_block()
    {
        // The third channel (index 2) starts at bit 362: byte 45, bits 2 and 3.
        CodeplugImage image = Load(BirdieKillerFixtures.ThirdChannelOn);
        new ChannelBits(image.Records).GetBits(362, 2).Should().Be(3);

        CodeplugFields f = CodeplugFields.Open(image);
        f.SetBirdieKiller(2, false);
        f.SetBirdieKiller(1, true);
        new ChannelBits(image.Records).GetBits(362, 2).Should().Be(0);
        new ChannelBits(image.Records).GetBits(181, 2).Should().Be(3);
        Enumerable.Range(0, 3).Select(f.GetBirdieKiller).Should().Equal(false, true, false);
    }

    [Fact]
    public void A_channel_with_only_the_first_bit_set_reads_on_and_unticking_clears_both_bits()
    {
        // Channels read off real radios often hold bit 0 alone (value 1), and measured on a radio that
        // bit is the one with the option's effect. Same layout in both mapped database versions.
        foreach (string dbVer in new[] { "0094", "0095" })
        {
            foreach (byte start in new byte[] { 0x01, 0x02 })
            {
                var image = new CodeplugImage(
                    new[] { new KeyValuePair<string, string>("DBVer", dbVer) },
                    new[] { new CodeplugRecord(0x05, 0, [(byte)(start | 0xEC), 0xEE, .. new byte[21]]) });
                CodeplugFields f = CodeplugFields.Open(image);

                f.GetBirdieKiller(0).Should().Be(start == 0x01, "bit 0 is the one that reads");
                f.SetBirdieKiller(0, false);
                image.Records[0].Data[0].Should().Be(0xEC, "off clears both bits and nothing else");
                f.GetBirdieKiller(0).Should().BeFalse();

                f.SetBirdieKiller(0, true);
                image.Records[0].Data[0].Should().Be(0xEF, "on writes both bits, as the CPS does");
                f.GetBirdieKiller(0).Should().BeTrue();
            }
        }
    }

    [Fact]
    public void A_split_channel_is_only_a_tx_frequency_that_differs_and_carries_no_birdie_killer()
    {
        CodeplugImage image = Load(BirdieKillerFixtures.SimplexAndSplit);
        image.ToM8p().Should().Be(BirdieKillerFixtures.SimplexAndSplit);
        CodeplugFields f = CodeplugFields.Open(image);

        f.ChannelCount.Should().Be(2);
        Enumerable.Range(0, 2).Select(f.GetBirdieKiller).Should().Equal(false, false);
        f.GetRxFrequencyHz(0).Should().Be(430_000_000);
        f.GetTxFrequencyHz(0).Should().Be(430_000_000);
        f.GetRxFrequencyHz(1).Should().Be(430_000_000);
        f.GetTxFrequencyHz(1).Should().Be(431_000_000);
        new ChannelBits(image.Records).GetBits(0, 2).Should().Be(0);
        new ChannelBits(image.Records).GetBits(181, 2).Should().Be(0);

        // Making the simplex channel split is the TX frequency alone.
        string before = image.ToM8p();
        FieldConsole.Set(f, "ch0.txfreq", "431000000");
        FieldConsole.Set(f, "ch0.txfreq", "430000000");
        image.ToM8p().Should().Be(before);
    }

    [Fact]
    public void Splittx_is_gone_and_says_where_it_went()
    {
        CodeplugFields f = CodeplugFields.Open(Load(BirdieKillerFixtures.ThirdChannelOn));
        FieldConsole.Describe(f).Select(r => r.Name).Should().NotContain(n => n.EndsWith(".splittx", StringComparison.Ordinal));

        foreach (Action act in new Action[]
        {
            () => FieldConsole.Get(f, "ch1.splittx"),
            () => FieldConsole.Set(f, "ch1.splittx", "true"),
        })
        {
            act.Should().Throw<FormatException>()
                .Which.Message.Should().Contain("ch1.birdiekiller").And.NotContain("\n");
        }
    }

    [Fact]
    public void Channel_add_copies_it_from_the_last_channel()
    {
        CodeplugImage image = Load(BirdieKillerFixtures.ThirdChannelOn);
        CodeplugFields f = CodeplugFields.Open(image);
        f.AddChannel().Should().Be(3);
        f.GetBirdieKiller(3).Should().BeTrue();

        f.SetBirdieKiller(3, false);
        f.AddChannel();
        f.GetBirdieKiller(4).Should().BeFalse();
    }

    [Fact]
    public void Get_and_dump_name_it_per_channel()
    {
        CodeplugFields f = CodeplugFields.Open(Load(BirdieKillerFixtures.FirstAndThirdChannelsOn));
        FieldConsole.Describe(f).Where(r => r.Name.EndsWith(".birdiekiller", StringComparison.Ordinal))
            .Should().Equal(("ch0.birdiekiller", "true"), ("ch1.birdiekiller", "false"), ("ch2.birdiekiller", "true"));

        FieldConsole.Set(f, "ch1.birdiekiller", "on");
        FieldConsole.Get(f, "ch1.birdiekiller").Should().Be("true");
        FieldConsole.Set(f, "ch1.birdiekiller", "off");
        FieldConsole.Get(f, "ch1.birdiekiller").Should().Be("false");
        ((Action)(() => FieldConsole.Set(f, "ch1.birdiekiller", "maybe"))).Should().Throw<FormatException>();
    }
}
