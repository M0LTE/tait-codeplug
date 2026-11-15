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

    [Fact]
    public void A_record_the_change_removes_is_reported_and_kept_in_the_backup()
    {
        CodeplugImage image = Fixtures.Open(Fixtures.DefaultDigitalIoTable).Image;
        LivePatch.Apply(image, "profile", "pdn-internal");   // programs F1, adding the programmed-key record
        var withKey = image.Records.Select(r => (r.Section, r.Index)).ToList();

        LivePatchResult patch = LivePatch.Apply(image, "key.f1", "Unassigned");

        var removed = withKey.Where(k => !image.Records.Any(r => (r.Section, r.Index) == k)).ToList();
        removed.Should().NotBeEmpty("clearing the only programmed key empties the programmed-key list");
        foreach ((byte section, byte index) in removed)
        {
            patch.Changed.Should().Contain($"0x{section:X2}/{index} (removed)");
        }

        patch.Original.Records.Select(r => (r.Section, r.Index)).Should().Equal(withKey);
        // and the backup is a consistent codeplug: it loads, and its key can be read and set again
        CodeplugFields restored = CodeplugFields.Open(CodeplugImage.LoadM8p(patch.Original.ToM8p()));
        FieldConsole.Get(restored, "key.f1").Should().Be("SquelchOverride");
        FieldConsole.Set(restored, "key.f1", "Unassigned");
    }

    [Fact]
    public void A_settable_field_with_no_readable_form_is_not_called_a_profile()
    {
        CodeplugImage image = Fixtures.Open(Fixtures.DefaultDigitalIoTable).Image;

        LivePatchResult patch = LivePatch.Apply(image, "audio", "packet-defaults");

        patch.Before.Should().Be("(not readable)");
        patch.After.Should().Be("packet-defaults");
    }
}
