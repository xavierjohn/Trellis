namespace Trellis.Asp.Authorization;

using System.Collections.Generic;
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
/// <b>Security:</b> This provider throws <see cref="InvalidOperationException"/>
/// unconditionally in non-Development environments, preventing accidental deployment
/// of the test bypass.
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
    ILogger<DevelopmentActorProvider> logger) : IActorProvider, IProvideActorVaryHeaders
{
    internal const string HeaderName = "X-Test-Actor";

    /// <inheritdoc />
    public IReadOnlyCollection<string> VaryByHeaders { get; } = [HeaderName];

    /// <inheritdoc />
    public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        ValidateEnvironment(cancellationToken);

        // DevelopmentActorProvider always yields a usable actor (default or parsed) — never
        // Maybe.None. That preserves the development convenience of running test requests
        // without sending an explicit X-Test-Actor header. Production providers (Claims/Entra)
        // return Maybe.None for the unauthenticated case; the development provider intentionally
        // does not, so dev workflows are unaffected by the 401 contract.
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return Task.FromResult(Maybe.From(CreateDefaultActor()));

        var headerValue = httpContext.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrEmpty(headerValue))
            return Task.FromResult(Maybe.From(CreateDefaultActor()));

        return Task.FromResult(Maybe.From(ParseActorFromHeader(headerValue)));
    }

    internal ValueTask<Maybe<ActorId>> ResolveCurrentActorIdAsync(CancellationToken cancellationToken)
    {
        ValidateEnvironment(cancellationToken);
        var header = httpContextAccessor.HttpContext?.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrEmpty(header))
            return ValueTask.FromResult(Maybe.From(ActorId.Create(options.Value.DefaultActorId)));

        try
        {
            if (JsonNode.Parse(header) is not JsonObject obj)
                return ValueTask.FromResult(Maybe.From(MalformedIdentity("JSON parsed to null or is not an object")));

            var id = ReadActorId(obj);
            if (id is null)
                return ValueTask.FromResult(Maybe.From(MalformedIdentity("Missing or empty 'Id' property")));

            // Lenient full resolution also falls back for malformed authorization payloads.
            if (!options.Value.ThrowOnMalformedHeader)
                ValidateAuthorizationValueShapes(obj);

            return ValueTask.FromResult(Maybe.From(id));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return ValueTask.FromResult(Maybe.From(MalformedIdentity(ex.Message)));
        }
    }

    private void ValidateEnvironment(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!hostEnvironment.IsDevelopment())
            throw new InvalidOperationException(
                "DevelopmentActorProvider is not allowed outside Development environments. " +
                "Use AddClaimsActorProvider or AddEntraActorProvider for production.");
    }

    private static ActorId? ReadActorId(JsonObject obj)
    {
        var id = GetPropertyCaseInsensitive(obj, "Id")?.GetValue<string>();
        return string.IsNullOrWhiteSpace(id) ? null : ActorId.Create(id);
    }

    private ActorId MalformedIdentity(string reason)
    {
        if (options.Value.ThrowOnMalformedHeader)
            throw new InvalidOperationException($"Malformed '{HeaderName}' header: {reason}.");

        LogMalformedHeader(logger, HeaderName, reason, options.Value.DefaultActorId);
        return ActorId.Create(options.Value.DefaultActorId);
    }

    private Actor ParseActorFromHeader(string headerValue)
    {
        try
        {
            var json = JsonNode.Parse(headerValue);
            if (json is not JsonObject obj)
                return HandleMalformedHeader(headerValue, "JSON parsed to null or is not an object");

            var id = ReadActorId(obj);
            if (id is null)
                return HandleMalformedHeader(headerValue, "Missing or empty 'Id' property");

            ValidateAuthorizationValueShapes(obj);
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

    private static void ValidateAuthorizationValueShapes(JsonObject obj)
    {
        if (GetPropertyCaseInsensitive(obj, "Permissions") is JsonArray permissions)
            ValidateStrings(permissions, "Permissions");
        if (GetPropertyCaseInsensitive(obj, "ForbiddenPermissions") is JsonArray forbidden)
            ValidateStrings(forbidden, "ForbiddenPermissions");
        if (GetPropertyCaseInsensitive(obj, "Attributes") is JsonObject attributes)
            ValidateStrings(attributes.Select(pair => pair.Value), "Attributes");

        static void ValidateStrings(IEnumerable<JsonNode?> nodes, string property)
        {
            foreach (var node in nodes)
                if (node is not null && node.GetValueKind() != JsonValueKind.String)
                    throw new InvalidOperationException($"'{property}' values must be JSON strings or null.");
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