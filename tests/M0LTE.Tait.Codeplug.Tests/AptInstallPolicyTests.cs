using M0LTE.Tait.Codeplug.Cli;

namespace M0LTE.Tait.Codeplug.Tests;

/// <summary>
/// `--upgrade` replaces the running binary in place. That is what a downloaded release wants and the
/// opposite of what a packaged one wants: the copy under /usr/bin belongs to dpkg, so overwriting it
/// desynchronises the package database and the next `apt install --only-upgrade` puts it back. The
/// rule that decides which kind of copy is running is worth pinning, because both ways of getting it
/// wrong are silent - refuse for a downloaded binary and --upgrade is simply broken, defer for one
/// dpkg does not own and the advice sends someone to a command that will not touch the file they ran.
/// </summary>
public class AptInstallPolicyTests
{
    private static Func<string, bool> MarkerPresent => path => path == AptInstallPolicy.MarkerPath;

    private static Func<string, bool> NothingExists => _ => false;

    [Theory]
    [InlineData("/usr/bin/tait-codeplug")]
    [InlineData("/usr/lib/tait-codeplug/tait-codeplug")]
    public void A_packaged_copy_defers_to_apt(string processPath)
    {
        AptInstallPolicy.ShouldDeferToApt(processPath, MarkerPresent)
            .Should().BeTrue("dpkg owns anything it installed under /usr, so apt has to do the upgrade");
    }

    [Theory]
    [InlineData("/home/tom/bin/tait-codeplug")]
    [InlineData("/opt/tait/tait-codeplug")]
    [InlineData("./tait-codeplug")]
    public void A_downloaded_copy_upgrades_itself_even_where_the_package_is_installed(string processPath)
    {
        // The marker only says the machine has the package. A copy someone downloaded to their own
        // directory is theirs, and --upgrade is the only way they have of updating it.
        AptInstallPolicy.ShouldDeferToApt(processPath, MarkerPresent)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("/usr/local/bin/tait-codeplug")]
    [InlineData("/usr/local/tait/tait-codeplug")]
    public void A_copy_under_usr_local_upgrades_itself(string processPath)
    {
        // Debian policy reserves /usr/local for the local administrator and dpkg never writes there,
        // so this is a hand-installed copy however many packages the machine has. Deferring would be
        // worse than useless: /usr/local/bin usually precedes /usr/bin on PATH, so apt would upgrade
        // a different file and the stale one would keep running.
        AptInstallPolicy.ShouldDeferToApt(processPath, MarkerPresent)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("/usr/bin/tait-codeplug")]
    [InlineData("/home/tom/bin/tait-codeplug")]
    public void No_marker_means_no_package_and_never_defers(string processPath)
    {
        AptInstallPolicy.ShouldDeferToApt(processPath, NothingExists)
            .Should().BeFalse("without the marker there is no apt install to defer to");
    }

    [Theory]
    [InlineData(@"C:\Program Files\tait-codeplug\tait-codeplug.exe")]
    [InlineData(@"C:\usr\bin\tait-codeplug.exe")]
    [InlineData("/Users/tom/bin/tait-codeplug")]
    [InlineData("/opt/homebrew/bin/tait-codeplug")]
    public void Windows_and_macos_paths_never_defer(string processPath)
    {
        // Belt and braces: the marker is a Linux path a .deb writes, so it should never be there on
        // these. If something does put a file at that path, the process path still has to be one
        // dpkg could have written for the refusal to fire.
        AptInstallPolicy.ShouldDeferToApt(processPath, MarkerPresent)
            .Should().BeFalse();
    }

    [Fact]
    public void An_unknown_process_path_never_defers()
    {
        // Environment.ProcessPath is null on some hosts. SelfUpgrade already refuses in that case for
        // its own reasons; this must not be what decides it.
        AptInstallPolicy.ShouldDeferToApt(null, MarkerPresent).Should().BeFalse();
        AptInstallPolicy.ShouldDeferToApt(string.Empty, MarkerPresent).Should().BeFalse();
    }

    [Fact]
    public void A_directory_named_like_the_prefix_is_not_the_prefix()
    {
        // /usrlocal and /usr-tools are not /usr/. The trailing slash in the comparison is what stops
        // this, so it is worth a test of its own.
        AptInstallPolicy.ShouldDeferToApt("/usrlocal/bin/tait-codeplug", MarkerPresent).Should().BeFalse();
        AptInstallPolicy.ShouldDeferToApt("/usr-tools/tait-codeplug", MarkerPresent).Should().BeFalse();
    }

    [Fact]
    public void It_only_ever_asks_about_the_marker()
    {
        // The probe is injected so this is a decision and not a filesystem call. If it ever starts
        // stat-ing other paths, a unit test is not testing what runs in production any more.
        var asked = new List<string>();

        AptInstallPolicy.ShouldDeferToApt("/usr/bin/tait-codeplug", path =>
        {
            asked.Add(path);
            return true;
        });

        asked.Should().Equal([AptInstallPolicy.MarkerPath]);
    }
}
