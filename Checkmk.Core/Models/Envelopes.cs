using System.Text.Json.Serialization;

namespace Checkmk.Core.Models;

/// <summary>
/// Checkmk-Collection-Antwort: /domain-types/{type}/collections/all liefert
/// { "value": [ ... ], "domainType": "...", ... }
/// </summary>
public sealed record CheckmkCollection<T>
{
    [JsonPropertyName("value")]
    public List<T> Value { get; init; } = [];

    [JsonPropertyName("domainType")]
    public string? DomainType { get; init; }
}

/// <summary>
/// Einzelnes Objekt (z. B. ein Host-Config). "extensions" enthaelt die
/// eigentlichen Attribute; "id"/"title" die Metadaten.
/// </summary>
public sealed record CheckmkObject<TExtensions>
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("extensions")]
    public TExtensions? Extensions { get; init; }
}

/// <summary>Antwort von GET /version.</summary>
public sealed record CheckmkVersionInfo
{
    [JsonPropertyName("site")]
    public string? Site { get; init; }

    [JsonPropertyName("group")]
    public string? Group { get; init; }

    [JsonPropertyName("rest_api")]
    public RestApiVersion? RestApi { get; init; }

    [JsonPropertyName("versions")]
    public VersionDetails? Versions { get; init; }

    [JsonPropertyName("edition")]
    public string? Edition { get; init; }

    [JsonPropertyName("demo")]
    public bool Demo { get; init; }
}

public sealed record RestApiVersion([property: JsonPropertyName("revision")] string? Revision);

public sealed record VersionDetails(
    [property: JsonPropertyName("checkmk")] string? Checkmk,
    [property: JsonPropertyName("python")] string? Python);
