using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

public class OsDetectionTests
{
    [Theory]
    [InlineData("Version: 2.5.0p2, OS: windows, TLS is not activated", OsFamily.Windows)]
    [InlineData("Version: 2.4.0, OS: linux", OsFamily.Linux)]
    [InlineData("OS: Windows", OsFamily.Windows)]
    [InlineData("agent output without os marker", OsFamily.Unknown)]
    [InlineData("", OsFamily.Unknown)]
    [InlineData(null, OsFamily.Unknown)]
    public void ParseFamily_reads_os_from_agent_output(string? output, OsFamily expected)
        => OsDetection.ParseFamily(output).Should().Be(expected);
}
