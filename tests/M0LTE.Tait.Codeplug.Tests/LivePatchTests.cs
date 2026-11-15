using AwesomeAssertions;
using M0LTE.Tait.Codeplug;
using M0LTE.Tait.Codeplug.Cli;
using Xunit;

namespace M0LTE.Tait.Codeplug.Tests;

/// <summary>
/// The read-modify step of <c>patch</c>: a field, a profile, and a profile that adds a record the
/// radio did not have (issue #18).
/// </summary>
public class LivePatchTests
{
    [Fact]
    public void A_profile_patches_without_reading_profile_as_a_field()
    {
        CodeplugImage image = Fixtures.Open(Fixtures.DefaultDigitalIoTable).Image;

        LivePatchResult patch = LivePatch.Apply(image, "profile", "pdn-internal");

        patch.Before.Should().Be("(profile)");
        patch.After.Should().Be("pdn-internal");
        patch.Changed.Should().Contain("0x3B/0");
        CodeplugFields.Open(image).DataPort.Should().Be(DataPort.InternalOptions);
    }

    [Fact]
    public void A_record_the_change_adds_counts_as_changed_and_stays_out_of_the_backup()
    {
        CodeplugImage image = Fixtures.Open(Fixtures.DefaultDigitalIoTable).Image;
        var before = image.Records.Select(r => (r.Section, r.Index)).ToHashSet();

        LivePatchResult patch = LivePatch.Apply(image, "profile", "pdn-internal");

        var added = image.Records.Select(r => (r.Section, r.Index)).Where(k => !before.Contains(k)).ToList();
        added.Should().NotBeEmpty("programming F1 appends to the programmed-key list, which a default codeplug lacks");
        foreach ((byte section, byte index) in added)
        {
            patch.Changed.Should().Contain($"0x{section:X2}/{index}");
        }

        patch.Original.Records.Select(r => (r.Section, r.Index)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public void The_backup_holds_the_bytes_as_read()
    {
        CodeplugImage image = Fixtures.Open(Fixtures.DefaultDigitalIoTable).Image;
        string audioBefore = Convert.ToHexString(image.Require(0x3B, 0).Data);

        LivePatchResult patch = LivePatch.Apply(image, "profile", "pdn-internal");

        Convert.ToHexString(image.Require(0x3B, 0).Data).Should().NotBe(audioBefore);
        Convert.ToHexString(patch.Original.Require(0x3B, 0).Data).Should().Be(audioBefore);
    }

    [Fact]
    public void A_field_reports_its_value_before_and_after()
    {
        CodeplugImage image = Fixtures.Open(Fixtures.DefaultDigitalIoTable).Image;

        LivePatchResult patch = LivePatch.Apply(image, "txtimer", "250");

        patch.After.Should().Be("250");
        patch.Before.Should().NotBe("250");
        patch.Changed.Should().Equal("0x15/0");
    }

    [Fact]
    public void An_unchanged_field_changes_nothing()
    {
        CodeplugImage image = Fixtures.Open(Fixtures.DefaultDigitalIoTable).Image;
        LivePatch.Apply(image, "txtimer", "250");

        LivePatchResult again = LivePatch.Apply(image, "txtimer", "250");

        again.Changed.Should().BeEmpty();
    }
}
