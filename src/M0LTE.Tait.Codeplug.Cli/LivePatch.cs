using M0LTE.Tait.Codeplug;

namespace M0LTE.Tait.Codeplug.Cli;

/// <summary>What one <c>patch</c> did to a codeplug read from a radio, before anything is written.</summary>
/// <param name="Before">The field's value before the change, <c>(profile)</c> for a profile, or <c>(not readable)</c> for a settable field with no readable form.</param>
/// <param name="After">The field's value after the change, or the profile's name.</param>
/// <param name="Changed">The records whose bytes changed, including any the change added, as <c>0xSS/i</c>, and any it removed, marked <c>(removed)</c>.</param>
/// <param name="Original">The codeplug as it was read, for the backup: every record the radio returned,
/// in read order with its original bytes, including any the change removed, and none it added.</param>
internal sealed record LivePatchResult(string Before, string After, IReadOnlyList<string> Changed, CodeplugImage Original);

/// <summary>
/// The read-modify part of <c>patch</c>, apart from the serial port so it can be tested. A change can
/// add a record as well as alter one: a profile that programs a function key appends an entry to the
/// programmed-key list (section 0x18), which a default codeplug does not have, and clearing the last
/// programmed key removes it again.
/// </summary>
internal static class LivePatch
{
    internal static LivePatchResult Apply(CodeplugImage image, string field, string value)
    {
        ArgumentNullException.ThrowIfNull(image);
        // A deep copy of every record as read, in read order: the backup, and the baseline for the diff.
        // A change can add records (a profile that programs a key appends to section 0x18) and remove
        // them (clearing the last programmed key empties 0x18), so neither side can be assumed.
        var read = image.Records.Select(r => new CodeplugRecord(r.Section, r.Index, (byte[])r.Data.Clone())).ToList();
        var snapshot = read.ToDictionary(r => (r.Section, r.Index), r => r.Data);
        CodeplugFields fields = CodeplugFields.Open(image);

        // "profile" is a verb of FieldConsole.Set, not a field, and a few settable fields have no
        // readable form: there is nothing to show before or after for either.
        string unreadable = field == "profile" ? "(profile)" : "(not readable)";
        string before = TryGet(fields, field) ?? unreadable;
        FieldConsole.Set(fields, field, value);
        string after = TryGet(fields, field) ?? value;

        var now = image.Records.ToDictionary(r => (r.Section, r.Index));
        var changed = image.Records
            .Where(r => !snapshot.TryGetValue((r.Section, r.Index), out byte[]? was) || !r.Data.AsSpan().SequenceEqual(was))
            .Select(r => $"0x{r.Section:X2}/{r.Index}")
            .Concat(read.Where(r => !now.ContainsKey((r.Section, r.Index))).Select(r => $"0x{r.Section:X2}/{r.Index} (removed)"))
            .ToList();

        var original = new CodeplugImage(image.Header, read);
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
