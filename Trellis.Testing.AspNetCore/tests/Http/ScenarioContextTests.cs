namespace Trellis.Testing.AspNetCore.Tests.Http;

using System.Collections.Generic;
using Trellis.Testing.AspNetCore.Http;

/// <summary>
/// Branch coverage for <see cref="ScenarioContext"/>: record validation, status/header/body
/// resolution, JSON traversal (including non-object encounters and missing properties), and
/// edge cases around malformed tokens.
/// </summary>
public class ScenarioContextTests
{
    private static ScenarioContext WithRecorded(
        string name = "create",
        int status = 201,
        string? body = "{\"id\":\"abc\",\"nested\":{\"value\":42},\"flag\":true,\"none\":null}",
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var ctx = new ScenarioContext();
        ctx.Record(name, status,
            headers ?? new Dictionary<string, string> { ["ETag"] = "\"v1\"", ["X-Trace-Id"] = "abc" },
            body);
        return ctx;
    }

    [Fact]
    public void Record_throws_on_null_or_empty_name()
    {
        var ctx = new ScenarioContext();
        FluentActions.Invoking(() => ctx.Record(null!, 200, new Dictionary<string, string>(), null))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ctx.Record("", 200, new Dictionary<string, string>(), null))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_with_invalid_json_body_keeps_raw_body_only()
    {
        var ctx = new ScenarioContext();
        ctx.Record("x", 200, new Dictionary<string, string>(), "not json");

        ctx.TryResolve("x.response.body", out var raw).Should().BeTrue();
        raw.Should().Be("not json");

        ctx.TryResolve("x.response.body.id", out _).Should().BeFalse();
    }

    [Fact]
    public void Record_with_null_or_blank_body_resolves_status_only()
    {
        var ctx = new ScenarioContext();
        ctx.Record("x", 200, new Dictionary<string, string>(), null);

        ctx.TryResolve("x.response.status", out var s).Should().BeTrue();
        s.Should().Be("200");

        ctx.TryResolve("x.response.body", out _).Should().BeFalse();
        ctx.TryResolve("x.response.body.foo", out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_returns_false_when_token_too_short()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create", out _).Should().BeFalse();
        ctx.TryResolve("create.response", out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_returns_false_when_named_response_unknown()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("other.response.body.id", out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_returns_false_when_second_segment_is_not_response()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create.request.body.id", out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_status_returns_status_code_as_string()
    {
        var ctx = WithRecorded(status: 418);
        ctx.TryResolve("create.response.status", out var v).Should().BeTrue();
        v.Should().Be("418");
    }

    [Fact]
    public void TryResolve_body_root_returns_raw_body_string()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create.response.body", out var v).Should().BeTrue();
        v.Should().Contain("\"id\"");
    }

    [Fact]
    public void TryResolve_body_dotted_path_returns_string_value()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create.response.body.id", out var v).Should().BeTrue();
        v.Should().Be("abc");
    }

    [Fact]
    public void TryResolve_body_nested_path_returns_number_raw()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create.response.body.nested.value", out var v).Should().BeTrue();
        v.Should().Be("42");
    }

    [Fact]
    public void TryResolve_body_returns_true_for_boolean()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create.response.body.flag", out var v).Should().BeTrue();
        v.Should().Be("true");
    }

    [Fact]
    public void TryResolve_body_null_value_returns_empty_string()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create.response.body.none", out var v).Should().BeTrue();
        v.Should().Be(string.Empty);
    }

    [Fact]
    public void TryResolve_body_path_descending_into_non_object_returns_false()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create.response.body.id.deeper", out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_body_missing_property_returns_false()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create.response.body.missing", out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_body_property_match_is_case_insensitive()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create.response.body.ID", out var v).Should().BeTrue();
        v.Should().Be("abc");
    }

    [Fact]
    public void TryResolve_headers_too_short_token_returns_false()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create.response.headers", out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_headers_exact_match()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create.response.headers.ETag", out var v).Should().BeTrue();
        v.Should().Be("\"v1\"");
    }

    [Fact]
    public void TryResolve_headers_dotted_name_is_joined()
    {
        var ctx = WithRecorded(headers: new Dictionary<string, string> { ["X.Y.Z"] = "ok" });
        ctx.TryResolve("create.response.headers.X.Y.Z", out var v).Should().BeTrue();
        v.Should().Be("ok");
    }

    [Fact]
    public void TryResolve_headers_case_insensitive_fallback()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create.response.headers.etag", out var v).Should().BeTrue();
        v.Should().Be("\"v1\"");
    }

    [Fact]
    public void TryResolve_unknown_kind_returns_false()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create.response.foo", out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_headers_unknown_name_returns_false()
    {
        var ctx = WithRecorded();
        ctx.TryResolve("create.response.headers.Missing", out _).Should().BeFalse();
    }

    [Fact]
    public void Record_overwrites_previous_named_response()
    {
        var ctx = new ScenarioContext();
        ctx.Record("x", 200, new Dictionary<string, string>(), "{\"a\":1}");
        ctx.Record("x", 201, new Dictionary<string, string>(), "{\"a\":2}");

        ctx.TryResolve("x.response.status", out var s).Should().BeTrue();
        s.Should().Be("201");
        ctx.TryResolve("x.response.body.a", out var v).Should().BeTrue();
        v.Should().Be("2");
    }

    [Fact]
    public void Record_repeated_overwrites_do_not_leak_resources_or_corrupt_state()
    {
        // Regression: previously each Record stored a JsonDocument that was never
        // disposed and was discarded on overwrite. The captured JSON state must
        // remain usable across many overwrites without throwing.
        var ctx = new ScenarioContext();
        for (int i = 0; i < 1000; i++)
        {
            ctx.Record("x", 200, new Dictionary<string, string>(), $"{{\"n\":{i}}}");
        }

        ctx.TryResolve("x.response.body.n", out var v).Should().BeTrue();
        v.Should().Be("999");
    }
}
