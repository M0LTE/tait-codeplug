using M0LTE.Tait.Codeplug;

namespace M0LTE.Tait.Codeplug.Cli;

/// <summary>
/// The check <c>write</c> makes before it sends a saved codeplug to a radio: the file must have come
/// from that radio. A codeplug read from one radio and written to another is the obvious mistake, and
/// the serial number in section 0 is what tells them apart.
/// </summary>
internal static class WriteGuard
{
    /// <summary>Null when the file may be written to the radio, otherwise why not.</summary>
    internal static string? Refusal(TaitIdentity radio, TaitIdentity file)
    {
        ArgumentNullException.ThrowIfNull(radio);
        ArgumentNullException.ThrowIfNull(file);
        if (string.IsNullOrEmpty(file.Serial))
        {
            return "the file carries no radio serial number (no section 0), so it cannot be matched to a radio. Read the radio with `read` and edit that file instead.";
        }

        if (string.IsNullOrEmpty(radio.Serial))
        {
            return "the radio did not report a serial number, so the file cannot be matched to it.";
        }

        return radio.Serial == file.Serial
            ? null
            : $"the radio is serial {radio.Serial} but the file was read from serial {file.Serial}.";
    }
}
