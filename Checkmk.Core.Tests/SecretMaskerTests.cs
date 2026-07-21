using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

public class SecretMaskerTests
{
    [Theory]
    [InlineData("register -H HOST -U Agent_cmk -P GeheimesPasswort", "-P ***")]
    [InlineData("CheckmkClient (User=automation2, Secret=sR12345@U)", "Secret=***")]
    [InlineData("password=hunter2 gesetzt", "password=***")]
    [InlineData("Passwort: hunter2", "Passwort: ***")]
    [InlineData("Authorization: Bearer abc.def.ghi", "Bearer ***")]
    [InlineData("token = xyz123", "token = ***")]
    public void Apply_masks_secrets(string input, string expectedFragment)
        => SecretMasker.Apply(input).Should().Contain(expectedFragment);

    [Fact]
    public void Apply_leaves_username_and_host_intact()
    {
        var masked = SecretMasker.Apply("register -H DBSQL01 -U Agent_cmk -P geheim");
        masked.Should().Contain("-H DBSQL01");
        masked.Should().Contain("-U Agent_cmk");
        masked.Should().NotContain("geheim");
    }

    [Fact]
    public void Apply_handles_null_and_empty()
    {
        SecretMasker.Apply(null).Should().BeEmpty();
        SecretMasker.Apply("").Should().BeEmpty();
    }
}
