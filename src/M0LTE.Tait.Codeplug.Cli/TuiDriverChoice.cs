using Terminal.Gui.Drivers;

namespace M0LTE.Tait.Codeplug.Cli;

/// <summary>
/// Which Terminal.Gui console driver the interactive mode should use.
///
/// Terminal.Gui picks one per platform, and on Windows that choice matters a lot more than it does
/// elsewhere. Every character typed into a text box repaints the whole screen (~7-8 bytes per cell,
/// so 22 KB at 100x30 and 82 KB at 200x50), and how expensive that is depends entirely on how the
/// driver hands it to the console: one buffered write is cheap, thousands of small console calls is
/// not, and on Windows the per-call cost is far higher than on a Unix pty.
///
/// So rather than guess which driver is quickest on someone else's console, this makes it a switch:
/// `tait-codeplug tui --driver ansi`, and `--driver list` to see what this platform offers.
/// </summary>
internal static class TuiDriverChoice
{
    /// <summary>
    /// Resolve a user-supplied driver name to the name Terminal.Gui knows, case-insensitively.
    /// Returns null for "default", meaning let the library choose as it always has.
    /// </summary>
    /// <exception cref="FormatException">The name is not one this platform supports.</exception>
    internal static string? Resolve(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)
            || requested.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string[] supported = SupportedNames();
        string? match = Array.Find(supported, n => n.Equals(requested.Trim(), StringComparison.OrdinalIgnoreCase));

        return match ?? throw new FormatException(
            $"unknown driver '{requested}'. This platform supports: {string.Join(", ", supported.Select(n => n.ToLowerInvariant()))}, "
            + "or 'default' to let Terminal.Gui choose.");
    }

    /// <summary>The driver names Terminal.Gui reports as usable on the machine this is running on.</summary>
    internal static string[] SupportedNames()
        => DriverRegistry.GetSupportedDrivers().Select(d => d.Name).ToArray();

    /// <summary>What <c>--driver list</c> prints.</summary>
    internal static void PrintAvailable(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine("console drivers available on this machine:");
        foreach (DriverRegistry.DriverDescriptor d in DriverRegistry.GetSupportedDrivers())
        {
            string isDefault = d.Name == DriverRegistry.GetDefaultDriver().Name ? "  (default here)" : string.Empty;
            output.WriteLine($"  {d.Name.ToLowerInvariant(),-10} {d.DisplayName}{isDefault}");
        }

        output.WriteLine();
        output.WriteLine("Use with: tait-codeplug tui --driver <name> [file.m8p]");
        output.WriteLine("Worth trying if typing into the editor feels slow: the drivers differ a lot in");
        output.WriteLine("how much work a screen repaint costs, and which is quickest depends on your console.");
    }
}
