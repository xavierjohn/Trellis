namespace Trellis.Asp;

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Writes an already-built RFC 9457 problem document straight to the response as
/// <c>application/problem+json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every Trellis failure ends up here, and the point of the shared helper is that none of them
/// reach MVC content negotiation. Trellis attaches <c>FieldViolationProblemDetail</c> and
/// <c>RuleViolationProblemDetail</c> values to <c>ProblemDetails.Extensions</c>, which is typed
/// <c>object?</c>. An application that registers <c>AddXmlDataContractSerializerFormatters()</c>
/// gives MVC a formatter that will attempt those values and throw, because
/// <c>DataContractSerializer</c> rejects runtime types it was not handed via
/// <c>KnownTypeAttribute</c> — and since the member is <c>object?</c>, no application can hand it
/// one. Left negotiable, a single <c>Accept</c> header turns any failure into an unhandled
/// exception, so the media type is not negotiable.
/// </para>
/// <para>
/// The document is serialized by its <em>runtime</em> type. A validation failure is an
/// <c>HttpValidationProblemDetails</c> or a <c>ValidationProblemDetails</c> whose <c>errors</c>
/// member is declared on the derived type alone; serializing against the <c>ProblemDetails</c>
/// static type would silently drop it.
/// </para>
/// <para>
/// Resolving the contract through <see cref="JsonSerializerOptions.GetTypeInfo(System.Type)"/>
/// keeps this trim- and AOT-clean without a suppression: whatever resolver the application already
/// configured supplies the contract, so a source-generated context is honoured where one exists and
/// no reflection is forced where one does not.
/// </para>
/// </remarks>
internal static class ProblemJsonWriter
{
    internal const string ContentType = "application/problem+json";

    internal static async Task WriteAsync(
        HttpContext httpContext,
        object problemDetails,
        JsonSerializerOptions serializerOptions)
    {
        JsonTypeInfo typeInfo = serializerOptions.GetTypeInfo(problemDetails.GetType());

        httpContext.Response.ContentType = ContentType;
        await JsonSerializer
            .SerializeAsync(httpContext.Response.Body, problemDetails, typeInfo, httpContext.RequestAborted)
            .ConfigureAwait(false);
    }
}
