using Checkmk.App.Services;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

public class StatusColumnFactoryTests
{
    /// <summary>Die Namen aus den Checkmk-Sichten muessen 1:1 uebernehmbar sein —
    /// genau dafuer ist die Config gedacht.</summary>
    [Theory]
    [InlineData("host")]
    [InlineData("service_display_name")]
    [InlineData("service_description")]
    [InlineData("service_state")]
    [InlineData("svc_check_age")]
    [InlineData("svc_state_age")]
    public void Checkmk_view_column_names_are_supported(string key)
        => StatusColumnFactory.IsKnown(key).Should().BeTrue();

    [Fact]
    public void Keys_are_case_insensitive()
        => StatusColumnFactory.IsKnown("Service_State").Should().BeTrue();

    [Fact]
    public void Unknown_key_is_rejected()
        => StatusColumnFactory.IsKnown("service_wunschkonzert").Should().BeFalse();

    [Fact]
    public void Default_columns_of_a_profile_are_all_buildable()
        => ViewerProfile.DefaultColumns.Should().OnlyContain(k => StatusColumnFactory.IsKnown(k));

    [Fact]
    public void Build_keeps_the_configured_order()
    {
        var columns = StatusColumnFactory.Build(["service_state", "host", "service_description"]);

        columns.Select(c => c.Header).Should().Equal("Status", "Host", "Service");
    }

    [Fact]
    public void Build_skips_unknown_keys_instead_of_throwing()
    {
        var columns = StatusColumnFactory.Build(["host", "gibtsnicht", "service_state"]);

        columns.Should().HaveCount(2);
    }
}
