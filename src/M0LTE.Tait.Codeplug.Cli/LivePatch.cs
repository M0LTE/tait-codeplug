using M0LTE.Tait.Codeplug;

namespace M0LTE.Tait.Codeplug.Cli;

/// <summary>What one <c>patch</c> did to a codeplug read from a radio, before anything is written.</summary>
/// <param name="Before">The field's value before the change, or <c>(profile)</c> for a profile, which has none.</param>
/// <param name="After">The field's value after the change, or the profile's name.</param>
/// <param name="Changed">The records whose bytes changed, including any the change added, as <c>0xSS/i</c>.</param>
/// <param name="Original">The codeplug as it was read, for the backup: every record the radio returned,
/// with its original bytes, and none the change added.</param>
internal sealed record LivePatchResult(string Before, string After, IReadOnlyList<string> Changed, CodeplugImage Original);

/// <summary>
/// The read-modify part of <c>patch</c>, apart from the serial port so it can be tested. A change can
/// add a record as well as alter one: a profile that programs a function key appends an entry to the
/// programmed-key list (section 0x18), which a default codeplug does not have.
/// </summary>
internal static class LivePatch
{
    internal static LivePatchResult Apply(CodeplugImage image, string field, string value)
    {
        ArgumentNullException.ThrowIfNull(image);
        var snapshot = image.Records.ToDictionary(r => (r.Section, r.Index), r => (byte[])r.Data.Clone());
        CodeplugFields fields = CodeplugFields.Open(image);

        // "profile" is a verb of FieldConsole.Set, not a field: there is nothing to read before or after.
        string before = TryGet(fields, field) ?? "(profile)";
        FieldConsole.Set(fields, field, value);
        string after = TryGet(fields, field) ?? value;

        var changed = image.Records
            .Where(r => !snapshot.TryGetValue((r.Section, r.Index), out byte[]? was) || !r.Data.AsSpan().SequenceEqual(was))
            .Select(r => $"0x{r.Section:X2}/{r.Index}")
            .ToList();

        var original = new CodeplugImage(
            image.Header,
            image.Records
                .Where(r => snapshot.ContainsKey((r.Section, r.Index)))
                .Select(r => new CodeplugRecord(r.Section, r.Index, snapshot[(r.Section, r.Index)]))
                .ToList());
        return new LivePatchResult(before, after, changed, original);
    }

    private static string? TryGet(CodeplugFields fields, string name)
    {
        try
        {
            return FieldConsole.Get(fields, name);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
