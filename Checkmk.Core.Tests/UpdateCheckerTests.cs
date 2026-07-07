using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

public class UpdateCheckerTagParsingTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V1.0.0", "1.0.0")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3+build.7", "1.2.3")]
    [InlineData("v1.2.3-alpha.0", "1.2.3")]
    [InlineData("v2.0.0.5", "2.0.0.5")]
    public void Strips_v_prefix_and_semver_metadata(string tag, string expected)
    {
        GitHubReleasesUpdateChecker.TryParseTag(tag, out var v).Should().BeTrue();
        v.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("nightly")]
    [InlineData("")]
    [InlineData("v")]
    public void Returns_false_for_unparsable_tags(string tag)
        => GitHubReleasesUpdateChecker.TryParseTag(tag, out _).Should().BeFalse();
}
