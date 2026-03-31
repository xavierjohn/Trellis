namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;

/// <summary>
/// Helpers for emitting Accept-Ranges response headers per RFC 9110 §14.3.
/// </summary>
public static class AcceptRangesExtensions
{
    /// <summary>Adds Accept-Ranges: bytes to the response.</summary>
    public static void AddAcceptRangesBytes(this HttpResponse response) =>
        response.Headers["Accept-Ranges"] = "bytes";

    /// <summary>Adds Accept-Ranges: none to the response.</summary>
    public static void AddAcceptRangesNone(this HttpResponse response) =>
        response.Headers["Accept-Ranges"] = "none";
}
