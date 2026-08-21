using System.Reflection;

namespace M0LTE.Tait.Codeplug.Cli;

/// <summary>The running version, as stamped by the release build (`-p:Version=...`). Shown in the
/// interactive mode's title bar and compared against the latest release by `--upgrade`.</summary>
internal static class CliVersion
{
    /// <summary>The informational version with any build metadata (`+sha`) trimmed off, or an empty
    /// string if the assembly carries none.</summary>
    internal static string Current
    {
        get
        {
            string? version = typeof(CliVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(version))
            {
                return string.Empty;
            }

            int plus = version.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? version : version[..plus];
        }
    }
}
