using M0LTE.Tait.Codeplug;

namespace M0LTE.Tait.Codeplug.Cli;

/// <summary>
/// The checks <c>write</c> makes before it sends a saved codeplug to a radio. The file must have come
/// from that radio (a codeplug read from one radio and written to another is the obvious mistake, and
/// the serial number in section 0 tells them apart), and on the database version the radio has now:
/// field offsets are version-specific, and a radio reflashed since the file was read would otherwise
/// take the old version's layout, version record included.
/// </summary>
internal static class WriteGuard
{
    /// <summary>Null when the file is fit to try at all, otherwise why not. Needs no radio, so it runs
    /// before the port is opened.</summary>
    internal static string? FileRefusal(CodeplugImage file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return string.IsNullOrEmpty(Identity(file).Serial)
            ? "the file carries no radio serial number (no section 0), so it cannot be matched to a radio. Read the radio with `read` and edit that file instead."
            : null;
    }

    /// <summary>Null when <paramref name="file"/> may be written to the radio that returned
    /// <paramref name="radio"/>, otherwise why not.</summary>
    internal static string? Refusal(CodeplugImage radio, CodeplugImage file)
    {
        ArgumentNullException.ThrowIfNull(radio);
        ArgumentNullException.ThrowIfNull(file);
        if (FileRefusal(file) is { } refusal)
        {
            return refusal;
        }

        TaitIdentity r = Identity(radio), f = Identity(file);
        if (string.IsNullOrEmpty(r.Serial))
        {
            return "the radio did not report a serial number, so the file cannot be matched to it.";
        }

        if (r.Serial != f.Serial)
        {
            return $"the radio is serial {r.Serial} but the file was read from serial {f.Serial}.";
        }

        if (r.Versions != f.Versions || r.Firmware != f.Firmware)
        {
            return $"the radio is now on firmware {r.Firmware}, versions {r.Versions}, but the file was read on firmware {f.Firmware}, versions {f.Versions}. Read the radio again and redo the edits on that file.";
        }

        CodeplugRecord? rv = radio.Records.FirstOrDefault(x => x.Section == 0x27);
        CodeplugRecord? fv = file.Records.FirstOrDefault(x => x.Section == 0x27);
        if (rv is null || fv is null || !rv.Data.AsSpan().SequenceEqual(fv.Data))
        {
            return "the file's database version record (0x27) does not match the radio's. Read the radio again and redo the edits on that file.";
        }

        return null;
    }

    private static TaitIdentity Identity(CodeplugImage image) =>
        TaitIdentity.FromSectionZero(image.Records.Where(r => r.Section == 0).ToList());
}
