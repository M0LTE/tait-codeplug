using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace M0LTE.Tait.Codeplug.Cli;

/// <summary>
/// `tait-codeplug --upgrade`: fetch the latest GitHub release for this platform and replace the
/// running executable with it.
///
/// The download is checked against the release's own <c>SHA256SUMS</c> before anything is replaced,
/// and the swap is a rename, so a failure anywhere leaves the existing binary untouched. Only makes
/// sense for the self-contained release binaries - a build tree gets told to use git instead.
/// </summary>
internal static class SelfUpgrade
{
    private const string Repo = "M0LTE/tait-codeplug";
    private const string ExecutableName = "tait-codeplug";

    internal static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        string? processPath = Environment.ProcessPath;
        if (processPath is null)
        {
            Console.Error.WriteLine("error: cannot work out which file is running, so cannot upgrade it.");
            return 2;
        }

        string fileName = Path.GetFileName(processPath);
        if (!fileName.Equals(ExecutableName, StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals(ExecutableName + ".exe", StringComparison.OrdinalIgnoreCase))
        {
            // Running out of `dotnet run` or a renamed copy: replacing that file with a release
            // binary would be surprising, so say what to do instead.
            Console.Error.WriteLine($"error: this is running as '{fileName}', not a released {ExecutableName} binary.");
            Console.Error.WriteLine("       --upgrade replaces a downloaded release; from a build tree, use git pull instead.");
            return 2;
        }

        string running = CliVersion.Current;
        string? rid = AssetRid();
        if (rid is null)
        {
            Console.Error.WriteLine($"error: no release build for this platform "
                + $"({RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}).");
            return 2;
        }

        // Check we can install before spending 40 MB of someone's bandwidth finding out we cannot.
        if (!CanInstallBeside(processPath, out string reason))
        {
            Console.Error.WriteLine($"error: cannot write to {Path.GetDirectoryName(processPath)}: {reason}");
            Console.Error.WriteLine("       re-run with sudo if it lives somewhere system-owned, or move it somewhere you own.");
            return 2;
        }

        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(5);
        http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(ExecutableName, string.IsNullOrEmpty(running) ? "0.0.0" : running));

        Console.WriteLine($"running {(string.IsNullOrEmpty(running) ? "(unknown version)" : running)} ({rid}), checking {Repo}...");

        (string tag, string version, IReadOnlyDictionary<string, string> assets) release;
        try
        {
            release = await LatestReleaseAsync(http, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            Console.Error.WriteLine($"error: could not read the latest release: {ex.Message}");
            return 2;
        }

        if (release.version == running)
        {
            Console.WriteLine($"already on the latest release ({release.tag}). Nothing to do.");
            return 0;
        }

        string assetName = $"{ExecutableName}-{release.version}-{rid}" + (rid.StartsWith("win", StringComparison.Ordinal) ? ".exe" : string.Empty);
        if (!release.assets.TryGetValue(assetName, out string? assetUrl))
        {
            Console.Error.WriteLine($"error: release {release.tag} has no asset named {assetName}.");
            Console.Error.WriteLine($"       it has: {string.Join(", ", release.assets.Keys)}");
            return 2;
        }

        Console.WriteLine($"upgrading {running} -> {release.version}");

        byte[] payload;
        string expectedHash;
        try
        {
            payload = await http.GetByteArrayAsync(assetUrl, cancellationToken).ConfigureAwait(false);

            if (!release.assets.TryGetValue("SHA256SUMS", out string? sumsUrl))
            {
                Console.Error.WriteLine($"error: release {release.tag} publishes no SHA256SUMS, so the download cannot be verified.");
                return 2;
            }

            string sums = await http.GetStringAsync(sumsUrl, cancellationToken).ConfigureAwait(false);
            if (!TryFindHash(sums, assetName, out expectedHash))
            {
                Console.Error.WriteLine($"error: SHA256SUMS has no entry for {assetName}.");
                return 2;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.Error.WriteLine($"error: download failed: {ex.Message}");
            return 2;
        }

        string actualHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            // Never install something whose bytes we cannot account for.
            Console.Error.WriteLine("error: checksum mismatch, refusing to install.");
            Console.Error.WriteLine($"       expected {expectedHash}");
            Console.Error.WriteLine($"       got      {actualHash}");
            return 2;
        }

        Console.WriteLine($"downloaded {payload.Length / (1024 * 1024)} MB, sha256 verified.");

        try
        {
            Install(processPath, payload);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: could not replace {processPath}: {ex.Message}");
            Console.Error.WriteLine("       if it lives somewhere system-owned, re-run with sudo, or move it somewhere you own.");
            return 2;
        }

        Console.WriteLine($"upgraded to {release.version}. The new binary is in place; this process is still the old one, so re-run it.");
        return 0;
    }

    /// <summary>Can we stage a file next to the target and rename it over? Checked up front, because
    /// the alternative is telling someone their install failed after a 40 MB download.</summary>
    private static bool CanInstallBeside(string target, out string reason)
    {
        string directory = Path.GetDirectoryName(target) ?? ".";
        string probe = Path.Combine(directory, $".{Path.GetFileName(target)}.writable-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>Stage the new bytes beside the target and rename them over it. A rename is atomic on
    /// the same filesystem, and on Unix it works even though the file is executing - the running
    /// process keeps the old inode. Windows will not let a running image be replaced, so the old one
    /// is renamed aside first and swept up by the next upgrade.</summary>
    private static void Install(string target, byte[] payload)
    {
        string directory = Path.GetDirectoryName(target) ?? ".";
        string staged = Path.Combine(directory, $".{Path.GetFileName(target)}.upgrade-{Guid.NewGuid():N}");
        string aside = target + ".old";

        File.WriteAllBytes(staged, payload);
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                // Carry the existing mode over rather than assuming 0755, so a locked-down install
                // stays locked down.
                File.SetUnixFileMode(staged, File.GetUnixFileMode(target));
                File.Move(staged, target, overwrite: true);
                return;
            }

            TryDelete(aside); // left over from a previous upgrade, once the process holding it exited
            File.Move(target, aside);
            try
            {
                File.Move(staged, target);
            }
            catch
            {
                File.Move(aside, target); // put it back; better a failed upgrade than no binary
                throw;
            }
        }
        finally
        {
            TryDelete(staged);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover file is untidy, not a failure.
        }
    }

    private static async Task<(string Tag, string Version, IReadOnlyDictionary<string, string> Assets)> LatestReleaseAsync(
        HttpClient http, CancellationToken cancellationToken)
    {
        string json = await http.GetStringAsync(
            $"https://api.github.com/repos/{Repo}/releases/latest", cancellationToken).ConfigureAwait(false);

        using JsonDocument document = JsonDocument.Parse(json);
        string tag = document.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;

        var assets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonElement asset in document.RootElement.GetProperty("assets").EnumerateArray())
        {
            string? name = asset.GetProperty("name").GetString();
            string? url = asset.GetProperty("browser_download_url").GetString();
            if (name is not null && url is not null)
            {
                assets[name] = url;
            }
        }

        return (tag, tag.TrimStart('v', 'V'), assets);
    }

    /// <summary>Pull one file's hash out of a `sha256sum` listing (`&lt;hash&gt;  &lt;name&gt;`).</summary>
    private static bool TryFindHash(string sums, string assetName, out string hash)
    {
        foreach (string line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == assetName)
            {
                hash = parts[0];
                return true;
            }
        }

        hash = string.Empty;
        return false;
    }

    /// <summary>The release asset suffix for the platform this is running on, or null if we do not
    /// publish one. Falls back to OS + architecture when the runtime RID is something we do not build
    /// (a musl or distro-specific RID, say).</summary>
    private static string? AssetRid()
    {
        string[] published = ["linux-x64", "linux-arm64", "linux-arm", "win-x64", "osx-x64", "osx-arm64"];

        string runtimeRid = RuntimeInformation.RuntimeIdentifier;
        if (Array.IndexOf(published, runtimeRid) >= 0)
        {
            return runtimeRid;
        }

        string? os = OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsMacOS() ? "osx"
            : null;
        string? architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => null,
        };

        if (os is null || architecture is null)
        {
            return null;
        }

        string guess = string.Create(CultureInfo.InvariantCulture, $"{os}-{architecture}");
        return Array.IndexOf(published, guess) >= 0 ? guess : null;
    }
}
