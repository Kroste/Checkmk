using Checkmk.Core;
using Checkmk.Core.Models;
using FluentAssertions;
using Xunit;

namespace Checkmk.Core.Tests;

public class CheckmkOptionsTests
{
    [Fact]
    public void BaseUri_uses_v1_path_and_https_by_default()
    {
        var opts = new CheckmkOptions
        {
            Host = "monitoring.lhp.intern", Site = "prod",
            Username = "automation", Secret = "x"
        };

        opts.BaseUri.ToString()
            .Should().Be("https://monitoring.lhp.intern/prod/check_mk/api/v1/");
    }

    [Fact]
    public void BaseUri_honours_http_and_custom_api_version()
    {
        var opts = new CheckmkOptions
        {
            Host = "cmk", Site = "test", Username = "a", Secret = "x",
            UseHttps = false, ApiVersion = "unstable"
        };

        opts.BaseUri.ToString().Should().Be("http://cmk/test/check_mk/api/unstable/");
    }
}

public class StateMappingTests
{
    [Theory]
    [InlineData(0, ServiceState.Ok)]
    [InlineData(1, ServiceState.Warning)]
    [InlineData(2, ServiceState.Critical)]
    [InlineData(3, ServiceState.Unknown)]
    public void Service_state_maps_from_livestatus_code(int code, ServiceState expected)
        => new ServiceStatus { State = code }.ServiceState.Should().Be(expected);

    [Theory]
    [InlineData(0, HostState.Up)]
    [InlineData(1, HostState.Down)]
    [InlineData(2, HostState.Unreachable)]
    public void Host_state_maps_from_livestatus_code(int code, HostState expected)
        => new HostStatus { State = code }.HostState.Should().Be(expected);

    [Fact]
    public void Acknowledged_and_downtime_flags_derive_correctly()
    {
        var svc = new ServiceStatus { Acknowledged = 1, ScheduledDowntimeDepth = 2 };
        svc.IsAcknowledged.Should().BeTrue();
        svc.InDowntime.Should().BeTrue();
    }
}

public class ServiceDiscoveryTests
{
    private static readonly System.Text.Json.JsonSerializerOptions Opts =
        new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

    [Fact]
    public void RunState_deserializes_from_object_envelope()
    {
        // GET /objects/service_discovery_run/{host_name} liefert einen
        // CheckmkObject<T>-Envelope mit "extensions".
        const string json = """
            {
                "id": "DBSQL01",
                "title": "Service discovery on DBSQL01",
                "extensions": { "active": false, "state": "finished" }
            }
            """;

        var envelope = System.Text.Json.JsonSerializer
            .Deserialize<CheckmkObject<ServiceDiscoveryRunState>>(json, Opts);

        envelope.Should().NotBeNull();
        envelope!.Extensions.Should().NotBeNull();
        envelope.Extensions!.Active.Should().BeFalse();
        envelope.Extensions.State.Should().Be("finished");
    }

    [Fact]
    public void Active_run_deserializes_with_active_true()
    {
        const string json = """
            { "id": "web01", "extensions": { "active": true, "state": "running" } }
            """;

        var envelope = System.Text.Json.JsonSerializer
            .Deserialize<CheckmkObject<ServiceDiscoveryRunState>>(json, Opts);

        envelope!.Extensions!.Active.Should().BeTrue();
        envelope.Extensions.State.Should().Be("running");
    }

    [Theory]
    [InlineData(ServiceDiscoveryMode.FixAll, "fix_all")]
    [InlineData(ServiceDiscoveryMode.New, "new")]
    [InlineData(ServiceDiscoveryMode.Remove, "remove")]
    [InlineData(ServiceDiscoveryMode.TabulaRasa, "tabula_rasa")]
    public void Mode_constants_use_checkmk_wire_values(string constant, string expected)
        => constant.Should().Be(expected);
}

public class CommentDeserializationTests
{
    private static readonly System.Text.Json.JsonSerializerOptions Opts =
        new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

    [Fact]
    public void Service_comment_extensions_deserialize_and_flag_as_service()
    {
        const string json = """
            {
                "id": "17",
                "extensions": {
                    "host_name": "DBSQL01",
                    "service_description": "CPU load",
                    "author": "cmkadmin",
                    "comment": "Wartung, siehe INC-4711",
                    "persistent": false,
                    "entry_time": "2026-01-15T10:30:00Z"
                }
            }
            """;

        var env = System.Text.Json.JsonSerializer
            .Deserialize<CheckmkObject<CommentExtensions>>(json, Opts);

        env!.Id.Should().Be("17");
        env.Extensions!.HostName.Should().Be("DBSQL01");
        env.Extensions.ServiceDescription.Should().Be("CPU load");
        env.Extensions.Author.Should().Be("cmkadmin");
        env.Extensions.IsService.Should().BeTrue();
        env.Extensions.Persistent.Should().BeFalse();
    }

    [Fact]
    public void Host_comment_has_no_service_description_and_is_not_service()
    {
        const string json = """
            {
                "id": "99",
                "extensions": {
                    "host_name": "web01",
                    "author": "alice",
                    "comment": "Reboot geplant",
                    "persistent": true
                }
            }
            """;

        var env = System.Text.Json.JsonSerializer
            .Deserialize<CheckmkObject<CommentExtensions>>(json, Opts);

        env!.Extensions!.ServiceDescription.Should().BeNull();
        env.Extensions.IsService.Should().BeFalse();
        env.Extensions.Persistent.Should().BeTrue();
    }
}

public class LivestatusHostFilterTests
{
    [Fact]
    public void Single_host_becomes_equality_query()
    {
        var f = new LivestatusHostFilter { IncludeHosts = new[] { "DB01" } };
        var json = f.ToJson();
        json.Should().Contain("\"op\":\"=\"");
        json.Should().Contain("\"left\":\"host_name\"");
        json.Should().Contain("\"right\":\"DB01\"");
    }

    [Fact]
    public void Multiple_hosts_become_or_expression()
    {
        var f = new LivestatusHostFilter { IncludeHosts = new[] { "DB01", "DB02", "DB03" } };
        var json = f.ToJson();
        json.Should().Contain("\"op\":\"or\"");
        json.Should().Contain("\"expr\":");
        json.Should().Contain("DB01");
        json.Should().Contain("DB02");
        json.Should().Contain("DB03");
    }

    [Fact]
    public void Regex_becomes_case_insensitive_match()
    {
        var f = new LivestatusHostFilter { HostNameRegex = ".*sql.*" };
        var json = f.ToJson();
        json.Should().Contain("\"op\":\"~~\"");
        json.Should().Contain("\"left\":\"host_name\"");
        json.Should().Contain("\".*sql.*\"");
    }

    [Fact]
    public void Include_list_wins_over_regex()
    {
        var f = new LivestatusHostFilter
        {
            IncludeHosts = new[] { "explicit" },
            HostNameRegex = "irrelevant"
        };
        f.ToJson().Should().NotContain("irrelevant");
    }

    [Fact]
    public void Empty_filter_produces_no_query()
    {
        new LivestatusHostFilter().ToJson().Should().BeNull();
        new LivestatusHostFilter().IsEmpty.Should().BeTrue();
    }
}

public class HostAttributesSerializationTests
{
    // Muss identisch zu CheckmkClient.JsonOpts sein.
    private static readonly System.Text.Json.JsonSerializerOptions Opts =
        new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

    [Fact]
    public void Empty_attributes_serialize_without_null_fields()
    {
        // Regression: Checkmk lehnt "ipaddress": null etc. mit
        // "These fields have problems: attributes" (HTTP 400) ab.
        var json = System.Text.Json.JsonSerializer.Serialize(new HostAttributes(), Opts);

        json.Should().Be("{}");
        json.Should().NotContain("null");
    }

    [Fact]
    public void Only_set_attributes_are_included()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new HostAttributes { IpAddress = "10.0.0.5" }, Opts);

        json.Should().Contain("ipaddress");
        json.Should().NotContain("alias");
        json.Should().NotContain("labels");
    }
}
