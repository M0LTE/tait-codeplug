using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace M0LTE.Tait.Codeplug.Cli;

/// <summary>
/// What the tool writes down when something fails, so that a failure is never just a tool that
/// vanished.
///
/// Interactive mode owns the whole terminal. An exception that escapes to the top takes the
/// application down, and by the time the runtime prints the trace the screen has already been handed
/// back, so on a lot of terminals the useful part is gone with it. That is how a real fault on a real
/// radio presented: "crashes just after putting the radio into programming mode, no error visible",
/// which is the one report nobody can act on. A read that fails intermittently is exactly the case
/// that needs the evidence kept, because the next run may well work.
///
/// So a failure is recorded in three places, deliberately overlapping: the in-app log while the
/// screen is still up, a file that outlives the process, and stderr after the terminal has been
/// restored. The file is the one that matters for a fault someone has to report second-hand.
/// </summary>
internal static class CrashReport
{
    /// <summary>The report's own file name. Timestamped so repeated failures do not overwrite each
    /// other, and sortable, because the interesting one is usually the last.</summary>
    internal static string FileName(DateTimeOffset when) =>
        string.Create(CultureInfo.InvariantCulture, $"tait-codeplug-crash-{when:yyyyMMdd-HHmmss-fff}.log");

    /// <summary>
    /// The whole report as text. Everything needed to act on a failure without being sat at the
    /// machine: what was being attempted, what the version was, what the platform is, and the
    /// exception with its stack and any inner exceptions (<c>ToString</c> carries all three).
    /// </summary>
    internal static string Format(string context, Exception exception, DateTimeOffset when)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var text = new StringBuilder();
        text.Append("tait-codeplug crash report").Append('\n');
        text.Append("when:     ").Append(when.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)).Append('\n');
        string version = CliVersion.Current;
        text.Append("version:  ").Append(version.Length == 0 ? "(unknown)" : version).Append('\n');
        text.Append("platform: ").Append(RuntimeInformation.OSDescription.Trim())
            .Append(' ').Append(RuntimeInformation.RuntimeIdentifier).Append('\n');
        text.Append("doing:    ").Append(context).Append('\n');
        text.Append('\n');
        text.Append(exception.ToString()).Append('\n');
        return text.ToString();
    }

    /// <summary>
    /// Write <paramref name="report"/> somewhere it will outlive the process, returning the path, or
    /// null if it could not be written. Best effort by design: this runs while something has already
    /// gone wrong, so it must never throw and add a second failure to the first. The caller still has
    /// the report text either way.
    /// </summary>
    internal static string? Write(string report, DateTimeOffset when)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), FileName(when));
            File.WriteAllText(path, report);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
