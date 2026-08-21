using AwesomeAssertions;
using M0LTE.Tait.Codeplug.Cli;
using Xunit;

namespace M0LTE.Tait.Codeplug.Tests;

/// <summary>
/// `--driver` exists because how much a screen repaint costs depends on the console driver, and which
/// one is quickest is not something that can be decided from here - it has to be tried on the machine
/// that is running it. So the only thing worth pinning down is that the argument is handled sanely:
/// the default stays the library's choice, names are not case-sensitive, and a typo says what the
/// options are rather than falling back silently to something else.
/// </summary>
public class TuiDriverChoiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    public void Nothing_asked_for_means_the_library_chooses(string? requested)
    {
        TuiDriverChoice.Resolve(requested).Should().BeNull();
    }

    [Fact]
    public void A_supported_name_resolves_whatever_case_it_is_given_in()
    {
        string canonical = TuiDriverChoice.SupportedNames()[0];

        TuiDriverChoice.Resolve(canonical).Should().Be(canonical);
        TuiDriverChoice.Resolve(canonical.ToLowerInvariant()).Should().Be(canonical);
        TuiDriverChoice.Resolve(canonical.ToUpperInvariant()).Should().Be(canonical);
        TuiDriverChoice.Resolve($"  {canonical}  ").Should().Be(canonical);
    }

    [Fact]
    public void An_unknown_name_is_refused_and_says_what_is_available()
    {
        Action resolve = () => TuiDriverChoice.Resolve("curses");

        resolve.Should().Throw<FormatException>()
            .WithMessage("*curses*")
            .WithMessage($"*{TuiDriverChoice.SupportedNames()[0].ToLowerInvariant()}*");
    }

    [Fact]
    public void Every_platform_offers_at_least_one_driver()
    {
        TuiDriverChoice.SupportedNames().Should().NotBeEmpty();
    }

    [Fact]
    public void The_listing_names_the_default_so_you_know_what_you_are_comparing_against()
    {
        var output = new StringWriter();

        TuiDriverChoice.PrintAvailable(output);

        string text = output.ToString();
        text.Should().Contain("(default here)");
        foreach (string name in TuiDriverChoice.SupportedNames())
        {
            text.Should().Contain(name.ToLowerInvariant());
        }
    }
}
