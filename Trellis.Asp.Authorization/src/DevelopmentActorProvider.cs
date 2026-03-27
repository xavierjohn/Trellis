namespace Trellis.Asp.Authorization;

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trellis.Authorization;

/// <summary>
/// <see cref="IActorProvider"/> implementation for development and testing that reads
/// actor identity from the <c>X-Test-Actor</c> HTTP header.
/// </summary>
/// <remarks>
/// <para>
/// <b>Security:</b> This provider throws <see cref="InvalidOperationException"/> if the
/// <c>X-Test-Actor</c> header is present in a Production environment, preventing accidental
/// deployment of the test bypass.
/// </para>
/// <para>
/// When no header is present, returns a configurable default actor (see <see cref="DevelopmentActorOptions"/>).
/// </para>
/// <para>
/// The header JSON schema matches the format produced by
/// <c>WebApplicationFactoryExtensions.CreateClientWithActor</c> in <c>Trellis.Testing</c>:
/// <code>
/// {
///   "Id": "user-1",
///   "Permissions": ["orders:create", "orders:read"],
///   "ForbiddenPermissions": [],
///   "Attributes": { "tid": "tenant-1" }
/// }
/// </code>
/// </para>
/// <para>
/// Register via <see cref="ServiceCollectionExtensions.AddDevelopmentActorProvider"/>.
/// </para>
/// </remarks>
public sealed partial class DevelopmentActorProvider(
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment hostEnvironment,
    IOptions<DevelopmentActorOptions> options,
    ILogger<DevelopmentActorProvider> logger) : IActorProvider
{
    internal const string HeaderName = "X-Test-Actor";

    /// <inheritdoc />
    public Actor GetCurrentActor()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return CreateDefaultActor();

        var headerValue = httpContext.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrEmpty(headerValue))
            return CreateDefaultActor();

        if (!hostEnvironment.IsDevelopment())
            throw new InvalidOperationException(
                $"The '{HeaderName}' header is not allowed outside Development environments. " +
                "Use a proper authentication provider (e.g., EntraActorProvider) in production.");

        return ParseActorFromHeader(headerValue);
    }

    private Actor ParseActorFromHeader(string headerValue)
    {
        try
        {
            var json = JsonNode.Parse(headerValue);
            if (json is not JsonObject obj)
                return HandleMalformedHeader(headerValue, "JSON parsed to null or is not an object");

            var id = GetPropertyCaseInsensitive(obj, "Id")?.GetValue<string>();
            if (string.IsNullOrEmpty(id))
                return HandleMalformedHeader(headerValue, "Missing or empty 'Id' property");

            var permissions = ParseStringArray(GetPropertyCaseInsensitive(obj, "Permissions"));
            var forbiddenPermissions = ParseStringArray(GetPropertyCaseInsensitive(obj, "ForbiddenPermissions"));
            var attributes = ParseStringDictionary(GetPropertyCaseInsensitive(obj, "Attributes"));

            return new Actor(id, permissions, forbiddenPermissions, attributes);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return HandleMalformedHeader(headerValue, ex.Message);
        }
    }

    private static JsonNode? GetPropertyCaseInsensitive(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node))
            return node;

        foreach (var kvp in obj)
        {
            if (string.Equals(kvp.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return null;
    }

    private Actor HandleMalformedHeader(string headerValue, string reason)
    {
        var config = options.Value;
        if (config.ThrowOnMalformedHeader)
            throw new InvalidOperationException(
                $"Malformed '{HeaderName}' header: {reason}.");

        LogMalformedHeader(logger, HeaderName, reason, config.DefaultActorId);

        return CreateDefaultActor();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Malformed '{HeaderName}' header ignored ({Reason}). Falling back to default actor '{DefaultActorId}'.")]
    private static partial void LogMalformedHeader(ILogger logger, string headerName, string reason, string defaultActorId);

    private Actor CreateDefaultActor()
    {
        var config = options.Value;
        return Actor.Create(config.DefaultActorId, config.DefaultPermissions);
    }

    private static HashSet<string> ParseStringArray(JsonNode? node)
    {
        if (node is not JsonArray array)
            return [];

        var result = new HashSet<string>(array.Count);
        foreach (var item in array)
        {
            var value = item?.GetValue<string>();
            if (value is not null)
                result.Add(value);
        }

        return result;
    }

    private static Dictionary<string, string> ParseStringDictionary(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return [];

        var result = new Dictionary<string, string>(obj.Count);
        foreach (var kvp in obj)
        {
            var value = kvp.Value?.GetValue<string>();
            if (value is not null)
                result[kvp.Key] = value;
        }

        return result;
    }
}