using System.Net;

namespace Checkmk.Core.Exceptions;

/// <summary>
/// Wird geworfen, wenn die REST-API einen Fehlerstatus (>= 400) liefert
/// oder die Antwort nicht wie erwartet deserialisiert werden kann.
/// </summary>
public sealed class CheckmkApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Roher Antwort-Body (bei Checkmk oft ein RFC7807 problem+json).</summary>
    public string? ResponseBody { get; }

    public CheckmkApiException(string message, HttpStatusCode? statusCode = null,
        string? responseBody = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
