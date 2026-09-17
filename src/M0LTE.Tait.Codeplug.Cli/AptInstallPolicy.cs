namespace M0LTE.Tait.Codeplug.Cli;

/// <summary>
/// Whether the running binary is the copy dpkg installed from the packet-net apt repository, in
/// which case <c>--upgrade</c> must refuse and point at apt instead.
///
/// <c>--upgrade</c> works by writing a downloaded release binary over
/// <see cref="Environment.ProcessPath"/>. That is right for a binary someone downloaded, and wrong
/// for one a package manager owns: dpkg's record of the file would no longer describe what is on
/// disk, and the next <c>apt install --only-upgrade</c> would put its own version back over the top.
/// So the packaged copy declines and says what to run.
///
/// The test is a marker file the .deb ships (<see cref="MarkerPath"/>, written by
/// packaging/build-deb.sh), not a shell-out to <c>dpkg -S</c>: no dependency on dpkg being
/// installed, deterministic, and nothing to parse.
///
/// Two conditions, not one, because the marker only says "this machine has the package", not "this
/// file came from it". Someone can have the package installed and also have downloaded a copy to
/// their home directory; that copy is theirs and <c>--upgrade</c> should work on it as normal. So
/// the path has to be a dpkg-owned one too.
///
/// <c>/usr/local</c> is excluded from that: Debian policy reserves it for the local administrator
/// and dpkg never writes there, so a binary under <c>/usr/local/bin</c> is a hand-installed copy
/// however many packages the machine has. Deferring on it would be actively misleading, because
/// <c>/usr/local/bin</c> usually comes first on PATH: apt would upgrade <c>/usr/bin</c>, the stale
/// copy would keep running, and the advice would have been wrong.
/// </summary>
internal static class AptInstallPolicy
{
    /// <summary>
    /// The marker the .deb ships. packaging/build-deb.sh writes this path; if one moves, move both.
    /// Its contents are the repository it came from, so the refusal can say where.
    /// </summary>
    internal const string MarkerPath = "/usr/share/tait-codeplug/installed-from-apt";

    /// <summary>Where to send someone whose copy came from apt, when the marker cannot be read.</summary>
    internal const string DefaultRepository = "https://packet-net.github.io/apt";

    /// <summary>Directories dpkg owns. A trailing slash on each, so /usrsomething cannot match.</summary>
    private const string SystemPrefix = "/usr/";

    /// <summary>Reserved for the local administrator; dpkg never installs here.</summary>
    private const string LocalPrefix = "/usr/local/";

    /// <summary>
    /// Should <c>--upgrade</c> defer to apt for the binary at <paramref name="processPath"/>?
    /// <paramref name="fileExists"/> is injected so this stays a decision and not a filesystem call.
    /// </summary>
    internal static bool ShouldDeferToApt(string? processPath, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);

        if (string.IsNullOrEmpty(processPath))
        {
            return false;
        }

        // Ordinal, and no case folding: these are Linux paths, where /USR is a different directory.
        if (!processPath.StartsWith(SystemPrefix, StringComparison.Ordinal)
            || processPath.StartsWith(LocalPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return fileExists(MarkerPath);
    }

    /// <summary>
    /// The repository named in the marker file, or <see cref="DefaultRepository"/> if it cannot be
    /// read. Best effort: this only decides what a message says, never whether to refuse.
    /// </summary>
    internal static string RepositoryName()
    {
        try
        {
            string first = File.ReadLines(MarkerPath).FirstOrDefault(string.Empty).Trim();
            return first.Length == 0 ? DefaultRepository : first;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return DefaultRepository;
        }
    }
}
