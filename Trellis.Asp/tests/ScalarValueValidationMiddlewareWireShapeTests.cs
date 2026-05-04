namespace Trellis.Asp.Tests;

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;

/// <summary>
/// Round-5 regression guard for PR #454: the JSON-deserialization 400 path
/// (<see cref="ScalarValueValidationMiddleware"/> → <c>WriteJsonDeserializationErrorAsync</c>)
/// must emit MVC dot+bracket field keys, matching every other Trellis.Asp
/// <c>ValidationProblem</c> emitter. Previously this path leaked
/// <see cref="JsonException.Path"/> values verbatim (e.g. <c>$.items[0].name</c>) and
/// used <c>"$"</c> for the root, leaving clients with two key shapes from one API.
/// </summary>
public sealed class ScalarValueValidationMiddlewareWireShapeTests
{
    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        return doc.RootElement.Clone();
    }

    private static Dictionary<string, string[]> ReadErrors(JsonElement root)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var prop in root.GetProperty("errors").EnumerateObject())
        {
            var values = new List<string>();
            foreach (var v in prop.Value.EnumerateArray())
                values.Add(v.GetString() ?? string.Empty);

            errors[prop.Name] = values.ToArray();
        }

        return errors;
    }

    [Fact]
    public async Task TrellisJsonValidationException_with_nested_path_emits_MVC_dot_bracket_key()
    {
        var ctx = NewContext();
        var inner = new TrellisJsonValidationException("Amount cannot be negative.");
        // System.Text.Json's JsonException.Path uses JSON Path notation: "$.foo[0].bar".
        // Set the protected setter via reflection.
        typeof(JsonException).GetProperty("Path")!.SetValue(inner, "$.items[0].amount");
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, inner);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);
        errors.Should().ContainKey("items[0].amount");
        errors.Should().NotContainKey("$.items[0].amount", "JSON Path '$.' prefix must be stripped on the wire");
    }

    [Fact]
    public async Task TrellisJsonValidationException_with_root_path_emits_empty_key()
    {
        var ctx = NewContext();
        var inner = new TrellisJsonValidationException("Body is required.");
        // No Path set -> represents the root document.
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, inner);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);
        errors.Should().ContainKey(string.Empty,
            "MVC convention represents the root via empty string, matching JsonPointerToMvc.Translate(\"\")");
        errors.Should().NotContainKey("$");
    }

    [Fact]
    public async Task plain_JsonException_emits_empty_key_for_invalid_body()
    {
        var ctx = NewContext();
        var inner = new JsonException("Unexpected token");
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, inner);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);
        errors.Should().ContainKey(string.Empty);
        errors.Should().NotContainKey("$");
    }

    [Fact]
    public async Task TrellisJsonValidationException_with_top_level_property_emits_unprefixed_MVC_key()
    {
        var ctx = NewContext();
        var inner = new TrellisJsonValidationException("Amount must be positive.");
        typeof(JsonException).GetProperty("Path")!.SetValue(inner, "$.amount");
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, inner);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);
        errors.Should().ContainKey("amount");
        errors.Should().NotContainKey("$.amount");
    }

    [Fact]
    public async Task plain_JsonException_with_populated_path_emits_MVC_dot_bracket_key()
    {
        // Common case: System.Text.Json's built-in failures (e.g. type conversion errors)
        // populate JsonException.Path automatically. The middleware MUST translate that to MVC
        // shape too — not only TrellisJsonValidationException paths.
        var ctx = NewContext();
        var inner = new JsonException("The JSON value could not be converted.");
        typeof(JsonException).GetProperty("Path")!.SetValue(inner, "$.items[0].amount");
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, inner);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);
        errors.Should().ContainKey("items[0].amount",
            "plain STJ JsonException.Path must also be translated to MVC convention");
        errors.Should().NotContainKey(string.Empty,
            "with a populated JsonException.Path the error must not collapse to the root key");
        errors["items[0].amount"][0].Should().Be("The request body contains invalid JSON.",
            "the curated message stays generic for non-Trellis JsonExceptions");
    }
}
