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

    [Theory]
    [InlineData("$['weird name']", "weird name")]
    [InlineData("$['a.b']", "a.b")]
    [InlineData("$['a/b']", "a/b")]
    [InlineData("$['a[0]']", "a[0]")]
    [InlineData("$.items[0]['weird name']", "items[0].weird name")]
    [InlineData("$['a''b']", "a'b")]
    [InlineData("$.foo['bar'].baz", "foo.bar.baz")]
    [InlineData("$['outer']['inner']", "outer.inner")]
    public async Task JsonException_with_bracket_quoted_property_segments_emits_MVC_key(
        string jsonExceptionPath, string expectedMvcKey)
    {
        // STJ uses JSONPath bracket-quoted syntax for property names containing characters
        // that aren't valid identifiers (space, dot, slash, bracket, etc.). Verified directly:
        // JsonSerializer.Deserialize<Dictionary<string,int>>("{\"weird name\":\"x\"}")
        //   throws JsonException with Path = "$['weird name']".
        // The middleware MUST translate these to MVC convention so the wire shape stays
        // consistent with JsonPointerToMvc.Translate output for equivalent field names.
        var ctx = NewContext();
        var inner = new JsonException("conversion failure");
        typeof(JsonException).GetProperty("Path")!.SetValue(inner, jsonExceptionPath);
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, inner);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);
        errors.Should().ContainKey(expectedMvcKey,
            $"path '{jsonExceptionPath}' should translate to MVC key '{expectedMvcKey}'");
        errors.Should().NotContainKey(jsonExceptionPath,
            "JSONPath bracket notation must not leak through to the wire");
    }

    [Fact]
    public async Task real_STJ_deserialization_failure_with_dot_in_property_name_emits_MVC_property_key()
    {
        // Integration-style guard: don't rely on reflection-set Path values. Trigger an actual
        // System.Text.Json deserialization failure on a property whose JSON name contains a dot
        // (forces STJ to emit JSONPath bracket-quoted notation: $['a.b']) and assert the
        // middleware's translator produces the bare property name on the wire.
        const string payload = "{\"a.b\":\"not-a-number\"}";
        using var doc = JsonDocument.Parse(payload);
        JsonException? captured = null;
        try
        {
            JsonSerializer.Deserialize<DotNamedModel>(payload);
        }
        catch (JsonException ex)
        {
            captured = ex;
        }

        captured.Should().NotBeNull("the deserialize call must throw a JsonException");
        captured!.Path.Should().Be("$['a.b']",
            "STJ should emit JSONPath bracket-quoted notation for property names containing '.'");

        var ctx = NewContext();
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, captured);
        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);
        errors.Should().ContainKey("a.b",
            "the bracket-quoted JSONPath segment must be unquoted to the bare property name");
    }

    private sealed class DotNamedModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("a.b")]
        public int Value { get; set; }
    }
}
