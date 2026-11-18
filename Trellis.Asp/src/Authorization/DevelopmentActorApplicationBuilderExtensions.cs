namespace Trellis.Asp.Authorization;

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Development-only HTTP validation for the supplied test-actor header.
/// </summary>
public static class DevelopmentActorApplicationBuilderExtensions
{
    /// <summary>
    /// Rejects invalid supplied <c>X-Test-Actor</c> headers as HTTP 400 Problem Details.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <remarks>
    /// Register before middleware or endpoints that resolve actors or actor identities.
    /// An absent header still permits the development provider's configured fallback.
    /// Supplied headers must contain exactly one JSON object with a nonblank string Id,
    /// optional string arrays for Permissions and ForbiddenPermissions, and an optional
    /// string-valued Attributes object. Null known fields/elements, invalid string escapes,
    /// and duplicate root or Attributes property names are rejected; unknown root properties
    /// are otherwise ignored.
    /// This opt-in HTTP policy does not change direct actor-provider calls or their options.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The host is not in Development.</exception>
    public static IApplicationBuilder UseDevelopmentActorHeaderValidation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.ApplicationServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
            throw new InvalidOperationException(
                "Development actor header validation is only allowed in Development environments.");

        return app.Use(ValidateHeaderAsync);
    }

    private static async Task ValidateHeaderAsync(HttpContext context, RequestDelegate next)
    {
        if (HasValidSuppliedHeader(context.Request.Headers))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var extensions = ProblemEnvelope.ForStatus(StatusCodes.Status400BadRequest);
        extensions["code"] = "development-actor.header.invalid";
        var problem = Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [DevelopmentActorProvider.HeaderName] =
                [
                    "Supply one JSON object with unique property names, a nonblank string Id, optional string arrays for " +
                    "Permissions and ForbiddenPermissions, and an optional string-valued Attributes object."
                ]
            },
            instance: context.Request.GetEncodedPathAndQuery(),
            statusCode: StatusCodes.Status400BadRequest,
            extensions: extensions);
        await ResponseFailureWriter.WriteProblemJsonAsync(context, problem).ConfigureAwait(false);
    }

    private static bool HasValidSuppliedHeader(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue(DevelopmentActorProvider.HeaderName, out var values))
            return true;
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            return false;

        try
        {
            using var document = JsonDocument.Parse(values[0]!);
            return IsValidActor(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsValidActor(JsonElement actor)
    {
        if (actor.ValueKind != JsonValueKind.Object)
            return false;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasId = false;
        foreach (var property in actor.EnumerateObject())
        {
            var name = property.Name;
            if (!names.Add(name))
                return false;

            if (string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(property.Value.GetString()))
                    return false;
                hasId = true;
            }
            else if (string.Equals(name, "Permissions", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "ForbiddenPermissions", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind != JsonValueKind.Array
                    || !property.Value.EnumerateArray().All(IsReadableString))
                    return false;
            }
            else if (string.Equals(name, "Attributes", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsStringDictionary(property.Value))
                    return false;
            }
            else
            {
                EnsureReadableStrings(property.Value);
            }
        }

        return hasId;
    }

    private static bool IsStringDictionary(JsonElement attributes)
    {
        if (attributes.ValueKind != JsonValueKind.Object)
            return false;

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in attributes.EnumerateObject())
            if (!names.Add(property.Name) || !IsReadableString(property.Value))
                return false;

        return true;
    }

    private static bool IsReadableString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is not null;

    private static void EnsureReadableStrings(JsonElement value)
    {
        // JsonDocument defers decoding escaped strings and property names until they are read.
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                _ = value.GetString();
                break;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    _ = property.Name;
                    EnsureReadableStrings(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                    EnsureReadableStrings(item);
                break;
        }
    }
}