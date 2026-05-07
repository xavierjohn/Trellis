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
    [InlineData("$.foo['bar'].baz", "foo.bar.baz")]
    [InlineData("$['outer']['inner']", "outer.inner")]
    // Embedded single quotes — STJ does NOT escape these by doubling, so the parser
    // must use a forward-scan-with-lookahead heuristic that closes only at "']"
    // followed by '.', '[', or end-of-string. Verified against real STJ:
    //   {"a'b":"x"} → $['a'b']
    //   {"a'b":{"foo":"x"}} → $['a'b'].foo
    //   {"a'b":[...]} → $['a'b'][0]
    //   {"a'.b":"x"} → $['a'.b']
    //   {"'":"x"} → $[''']  (STJ output is genuinely ambiguous here)
    [InlineData("$['a'b']", "a'b")]
    [InlineData("$['a'b'].foo", "a'b.foo")]
    [InlineData("$['a'b'][0]", "a'b[0]")]
    [InlineData("$['a'.b']", "a'.b")]
    [InlineData("$[''']", "'")]
    [InlineData("$[''a']", "'a")]
    [InlineData("$['a'']", "a'")]
    [InlineData("$['a']b']", "a']b")]
    public async Task JsonException_with_bracket_quoted_property_segments_emits_MVC_key(
        string jsonExceptionPath, string expectedMvcKey)
    {
        // STJ uses JSONPath bracket-quoted syntax for property names containing characters
        // that aren't valid identifiers (space, dot, slash, bracket, single quote, etc.).
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
    }

    [Theory]
    // Empty STJ path segments — verified against real STJ:
    //   {"":"x"} → $.
    //   {"":{"foo":"x"}} → $..foo
    //   {"foo":{"":"x"}} → $.foo.
    //   {"":{"":"x"}} → $..
    //   {"":[...]} → $.
    // These must map to JsonPointerToMvc.Translate("/") => [""] semantics so the wire
    // key for empty property names is consistent across emitters.
    [InlineData("$.", "[\"\"]")]
    [InlineData("$..foo", "[\"\"].foo")]
    [InlineData("$.foo.", "foo[\"\"]")]
    [InlineData("$..", "[\"\"][\"\"]")]
    [InlineData("$['']", "[\"\"]")]
    [InlineData("$.foo['']", "foo[\"\"]")]
    [InlineData("$[''].foo", "[\"\"].foo")]
    public async Task JsonException_with_empty_property_segments_emits_MVC_empty_indexer(
        string jsonExceptionPath, string expectedMvcKey)
    {
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
            $"path '{jsonExceptionPath}' should translate to MVC key '{expectedMvcKey}' "
            + "(matching JsonPointerToMvc.Translate output for the equivalent JSON Pointer)");
    }

    [Fact]
    public async Task real_STJ_deserialization_failure_with_empty_dictionary_key_emits_MVC_empty_indexer()
    {
        // Integration-style guard for finding 2 of GPT-5.5 round-7 review.
        // STJ emits "$." for an empty dictionary key (verified) — the middleware must
        // translate it to the JSON Pointer-equivalent `[""]` shape.
        const string payload = "{\"\":\"not-a-number\"}";
        JsonException? captured = null;
        try
        {
            JsonSerializer.Deserialize<Dictionary<string, int>>(payload);
        }
        catch (JsonException ex)
        {
            captured = ex;
        }

        captured.Should().NotBeNull("the deserialize call must throw a JsonException");
        captured!.Path.Should().Be("$.",
            "STJ emits a trailing-dot path for an empty dictionary key");

        var ctx = NewContext();
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, captured);
        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);
        errors.Should().ContainKey("[\"\"]",
            "the empty STJ path segment must produce [\"\"] to match JsonPointerToMvc.Translate(\"/\")");
    }

    [Fact]
    public async Task TrellisJsonValidationException_with_FieldViolations_emits_per_field_MVC_keys()
    {
        // F9 regression guard (lab feedback round 2): when a composite VO converter
        // (e.g. ShippingAddress) fails multi-field validation during deserialization, the
        // wire response MUST emit one entry per leaf — keyed by `<parentPath>.<leaf>` — and
        // MUST NOT collapse all leaves into a single ;-joined string under the parent path.
        //
        // Today's broken behaviour (observed by Opus 4.7 lab run on 2026-05-06):
        //   "errors": {
        //     "$.shippingAddress": [
        //       "/street: Street is required.; /city: City is required.; /state: ..."
        //     ]
        //   }
        //
        // Required behaviour:
        //   "errors": {
        //     "shippingAddress.street": ["Street is required."],
        //     "shippingAddress.city":   ["City is required."],
        //     "shippingAddress.state":  ["State is required."]
        //   }
        //
        // The composite VO converter must carry the structured `Error.UnprocessableContent`
        // on the thrown `TrellisJsonValidationException` (via the new `UnprocessableContent`
        // init property) so the middleware can emit per-leaf entries instead of one opaque
        // joined string.
        var ctx = NewContext();
        var fields = EquatableArray.Create(
        [
            new FieldViolation(InputPointer.ForProperty("street"), "validation.error") { Detail = "Street is required." },
            new FieldViolation(InputPointer.ForProperty("city"),   "validation.error") { Detail = "City is required." },
            new FieldViolation(InputPointer.ForProperty("state"),  "validation.error") { Detail = "State is required." },
        ]);
        var error = new Error.UnprocessableContent(fields, EquatableArray<RuleViolation>.Empty)
        {
            Detail = "ShippingAddress validation failed.",
        };

        var inner = new TrellisJsonValidationException(error.GetDisplayMessage())
        {
            UnprocessableContent = error,
        };
        typeof(JsonException).GetProperty("Path")!.SetValue(inner, "$.shippingAddress");
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, inner);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);

        errors.Should().ContainKey("shippingAddress.street");
        errors.Should().ContainKey("shippingAddress.city");
        errors.Should().ContainKey("shippingAddress.state");

        errors["shippingAddress.street"].Should().Equal(["Street is required."]);
        errors["shippingAddress.city"].Should().Equal(["City is required."]);
        errors["shippingAddress.state"].Should().Equal(["State is required."]);

        errors.Should().NotContainKey("shippingAddress",
            "per-field entries must replace any single ;-joined entry under the parent path");
        errors.Should().NotContainKey("$.shippingAddress",
            "JSON Path '$.' prefix must be stripped on the wire — bug #1 of F9");
    }

    [Fact]
    public async Task TrellisJsonValidationException_with_FieldViolations_at_root_emits_unprefixed_per_field_keys()
    {
        // Edge case: the structured error is at the root document (parent path "$" / empty).
        // Per-leaf keys must NOT be prefixed with a leading dot ("." or ".street"); they
        // must equal the raw leaf name (matching JsonPointerToMvc.Translate("/street") == "street").
        var ctx = NewContext();
        var fields = EquatableArray.Create(
        [
            new FieldViolation(InputPointer.ForProperty("amount"),   "validation.error") { Detail = "Amount must be positive." },
            new FieldViolation(InputPointer.ForProperty("currency"), "validation.error") { Detail = "Currency must be ISO 4217." },
        ]);
        var error = new Error.UnprocessableContent(fields, EquatableArray<RuleViolation>.Empty);

        var inner = new TrellisJsonValidationException(error.GetDisplayMessage())
        {
            UnprocessableContent = error,
        };
        // Root path — JsonException.Path stays default ("$" or null).
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, inner);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);

        errors.Should().ContainKey("amount");
        errors.Should().ContainKey("currency");
        errors["amount"].Should().Equal(["Amount must be positive."]);
        errors["currency"].Should().Equal(["Currency must be ISO 4217."]);
        errors.Should().NotContainKey(string.Empty,
            "with structured field violations the root key must not collapse to an empty entry");
    }

    [Fact]
    public async Task TrellisJsonValidationException_with_FieldViolation_no_Detail_uses_ReasonCode()
    {
        // Coverage: exercises the `!string.IsNullOrEmpty(fv.Detail) ? fv.Detail : fv.ReasonCode`
        // fallback when a FieldViolation has no Detail set. The wire entry must surface the
        // ReasonCode so the client still sees a non-empty error string.
        var ctx = NewContext();
        var fields = EquatableArray.Create(
        [
            new FieldViolation(InputPointer.ForProperty("amount"), "amount.required"),  // no Detail
        ]);
        var error = new Error.UnprocessableContent(fields, EquatableArray<RuleViolation>.Empty);

        var inner = new TrellisJsonValidationException(error.GetDisplayMessage())
        {
            UnprocessableContent = error,
        };
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, inner);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);

        errors.Should().ContainKey("amount");
        errors["amount"].Should().Equal(["amount.required"],
            "when Detail is empty, the ReasonCode must surface as the wire detail");
    }

    [Fact]
    public async Task TrellisJsonValidationException_with_two_violations_at_same_field_emits_merged_array()
    {
        // Coverage: exercises the `if (perLeafErrors.TryGetValue(combinedKey, out var existing))`
        // merge branch. Two FieldViolations targeting the same leaf path must produce a single
        // wire key with both messages in the array — not overwrite each other or split into
        // duplicate keys.
        var ctx = NewContext();
        var fields = EquatableArray.Create(
        [
            new FieldViolation(InputPointer.ForProperty("street"), "street.required") { Detail = "Street is required." },
            new FieldViolation(InputPointer.ForProperty("street"), "street.too-short") { Detail = "Street must be at least 3 characters." },
        ]);
        var error = new Error.UnprocessableContent(fields, EquatableArray<RuleViolation>.Empty);

        var inner = new TrellisJsonValidationException(error.GetDisplayMessage())
        {
            UnprocessableContent = error,
        };
        typeof(JsonException).GetProperty("Path")!.SetValue(inner, "$.shippingAddress");
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, inner);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);

        errors.Should().ContainKey("shippingAddress.street");
        errors["shippingAddress.street"].Should().BeEquivalentTo([
            "Street is required.",
            "Street must be at least 3 characters.",
        ], "two violations at the same leaf path must merge into one array entry");
    }

    [Fact]
    public async Task TrellisJsonValidationException_with_root_pointer_leaf_uses_parent_only()
    {
        // Coverage: exercises `CombineMvcKeys` `string.IsNullOrEmpty(leaf)` early-return branch.
        // FieldViolation with InputPointer.Root has Field.Path = "" which JsonPointerToMvc
        // translates to ""; combining "shippingAddress" + "" must yield "shippingAddress",
        // not "shippingAddress." (with trailing dot).
        var ctx = NewContext();
        var fields = EquatableArray.Create(
        [
            new FieldViolation(InputPointer.Root, "address.invalid") { Detail = "Address is malformed." },
        ]);
        var error = new Error.UnprocessableContent(fields, EquatableArray<RuleViolation>.Empty);

        var inner = new TrellisJsonValidationException(error.GetDisplayMessage())
        {
            UnprocessableContent = error,
        };
        typeof(JsonException).GetProperty("Path")!.SetValue(inner, "$.shippingAddress");
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, inner);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);

        errors.Should().ContainKey("shippingAddress");
        errors["shippingAddress"].Should().Equal(["Address is malformed."]);
        errors.Should().NotContainKey("shippingAddress.",
            "empty leaf must not produce a trailing-dot key");
    }

    [Fact]
    public async Task TrellisJsonValidationException_with_RulesOnly_falls_back_to_unstructured_entry()
    {
        // Review feedback (PR #474, comment 1): when an Error.UnprocessableContent has only
        // RuleViolations and no FieldViolations (e.g., produced by Error.UnprocessableContent.ForRule(...)),
        // the structured per-leaf branch must NOT swallow the validation message into an empty
        // `errors` object. Fall back to the unstructured single-entry shape under the parent path
        // with the curated exception message intact.
        var ctx = NewContext();
        var rules = EquatableArray.Create(
        [
            new RuleViolation("order.total.exceeds-credit-limit") { Detail = "Order total exceeds the customer's credit limit." },
        ]);
        var error = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty, rules);

        var inner = new TrellisJsonValidationException(error.GetDisplayMessage())
        {
            UnprocessableContent = error,
        };
        typeof(JsonException).GetProperty("Path")!.SetValue(inner, "$.order");
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, inner);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);

        errors.Should().NotBeEmpty(
            "rules-only structured errors must surface the curated message — not collapse to an empty errors object");
        errors.Should().ContainKey("order");
        errors["order"][0].Should().Contain("credit limit",
            "the curated exception message (or rule detail) must be visible to the client");
    }

    [Theory]
    [InlineData("/0", "items[0]")]
    [InlineData("/0/name", "items[0].name")]
    [InlineData("/", "items[\"\"]")]
    public async Task TrellisJsonValidationException_with_indexer_leaf_emits_MVC_indexer_concat(
        string fieldPointer, string expectedKey)
    {
        // Review feedback (PR #474, comment 2): MVC convention is `items[0]`, not `items.[0]`.
        // When the leaf MVC key starts with '[', concatenation with the parent must NOT insert
        // a '.' separator. Same for empty-leaf indexer (`items[""]`).
        var ctx = NewContext();
        var fields = EquatableArray.Create(
        [
            new FieldViolation(new InputPointer(fieldPointer), "validation.error") { Detail = "Invalid line item." },
        ]);
        var error = new Error.UnprocessableContent(fields, EquatableArray<RuleViolation>.Empty);

        var inner = new TrellisJsonValidationException(error.GetDisplayMessage())
        {
            UnprocessableContent = error,
        };
        typeof(JsonException).GetProperty("Path")!.SetValue(inner, "$.items");
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, inner);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);

        errors.Should().ContainKey(expectedKey,
            $"indexer leaf '{fieldPointer}' under parent 'items' must produce '{expectedKey}', not 'items.{expectedKey.Substring(5)}'");
        var brokenKey = $"items.{expectedKey.Substring("items".Length)}";
        errors.Should().NotContainKey(brokenKey,
            "MVC indexer keys must not contain a '.' separator before '['");
    }

    [Fact]
    public async Task real_STJ_deserialization_failure_with_single_quote_in_property_name_emits_MVC_property_key()
    {
        // Integration-style guard for finding 1 of GPT-5.5 round-7 review.
        // STJ does NOT escape embedded single quotes in bracket-quoted JSONPath segments;
        // it emits "$['a'b']" for a dictionary key "a'b". The forward-scan tokenizer
        // must recover the property name correctly.
        const string payload = "{\"a'b\":\"not-a-number\"}";
        JsonException? captured = null;
        try
        {
            JsonSerializer.Deserialize<Dictionary<string, int>>(payload);
        }
        catch (JsonException ex)
        {
            captured = ex;
        }

        captured.Should().NotBeNull("the deserialize call must throw a JsonException");
        captured!.Path.Should().Be("$['a'b']",
            "STJ emits the embedded single quote without escaping");

        var ctx = NewContext();
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, captured);
        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        var problem = await ReadProblemAsync(ctx);
        var errors = ReadErrors(problem);
        errors.Should().ContainKey("a'b",
            "the embedded single-quote property name must round-trip through the tokenizer");
    }

    private sealed class DotNamedModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("a.b")]
        public int Value { get; set; }
    }

    [Theory]
    // Documents the deliberate lossiness in JsonPathToMvcKey for property names containing
    // the literal sequence '][. STJ does not escape these characters, so the path output
    // is genuinely ambiguous between "multiple segments" and "single segment with embedded
    // '][". The heuristic prefers the multi-segment interpretation because legitimate
    // adjacent non-identifier property names (e.g. $['weird name']['another weird'])
    // are common; property names containing literal '][ are not. Pinning the current
    // behavior here so any future change is intentional.
    [InlineData("$['a'][']", "a.]")]               // STJ emits this for dict key "a'][" (lossy → multi-segment + malformed tail)
    [InlineData("$['a'][b']", "a[b']")]            // STJ emits this for dict key "a'][b" (lossy → multi-segment + malformed tail)
    [InlineData("$['a'.b']['foo']", "a'.b.foo")]   // STJ emits this for dict key "a'.b']['foo" (lossy → split on '][)
    public async Task JsonException_with_property_name_containing_quote_bracket_sequence_uses_multi_segment_interpretation(
        string jsonExceptionPath, string expectedMvcKey)
    {
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
            "STJ path output is genuinely lossy for property names containing '][; "
            + "the heuristic prefers the multi-segment interpretation as a deliberate trade-off");
    }
}
