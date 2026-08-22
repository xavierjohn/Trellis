namespace Trellis.Asp.Tests;

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis;
using Trellis.Primitives;

/// <summary>
/// Pins the status-code split between binder-level VO validation failures (semantic — RFC 9110
/// §15.5.21 "Unprocessable Content", 422) and JSON syntax errors (RFC 9110 §15.5.1 "Bad Request",
/// 400). Before this fix, every binder validation failure — including <see cref="TrellisJsonValidationException"/>
/// thrown by <c>CompositeValueObjectJsonConverter</c> when value-level rules failed, and scalar-VO
/// <c>TryCreate</c> failures on query/route parameters — surfaced as 400, while the same logical
/// "input is invalid" failure occurring later in a domain handler returned 422 via
/// <c>ResponseFailureWriter</c>. Clients had to special-case both. The failure shape is now
/// status-aligned with the rest of the framework: only well-formed-bytes-but-invalid-JSON-tokens
/// returns 400.
/// </summary>
public sealed class BinderValidationStatusCodeTests
{
    private static IHost CreateMvcHost(System.Action<TrellisAspOptions>? configure = null)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddProblemDetails();
                    s.AddTrellisAspWithScalarValidation(configure ?? (_ => { }));
                    s.AddControllers().AddApplicationPart(typeof(StatusCodeController).Assembly);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return builder.Start();
    }

    [Fact]
    public async Task Mvc_CompositeVoValidationFailure_Returns422WithRfcCompliantProblemDetails()
    {
        using var host = CreateMvcHost();
        using var client = host.GetTestClient();

        var json = """{"address":{"street":"","city":"","state":""}}""";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("/binder-status/composite", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertRfc9457ProblemDetails(
            resp,
            expectedStatus: 422,
            expectedErrorKey: "address.street",
            expectedInstance: "/binder-status/composite");
    }

    [Fact]
    public async Task Mvc_ScalarVoQueryValidationFailure_Returns422()
    {
        using var host = CreateMvcHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/binder-status/scalar?value=", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertRfc9457ProblemDetails(
            resp,
            expectedStatus: 422,
            expectedInstance: "/binder-status/scalar?value=");
    }

    [Fact]
    public async Task Mvc_MalformedJson_Returns400()
    {
        using var host = CreateMvcHost();
        using var client = host.GetTestClient();

        // Truncated/malformed JSON — System.Text.Json raises a plain JsonException, not a
        // TrellisJsonValidationException. The bytes are not valid JSON, so 400 is correct
        // per RFC 9110 §15.5.1 ("syntactically malformed message").
        var json = "{not valid";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("/binder-status/composite", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertRfc9457ProblemDetails(
            resp,
            expectedStatus: 400,
            expectedInstance: "/binder-status/composite");
    }

    [Fact]
    public async Task Mvc_MaybeScalarVoQueryValidationFailure_Returns422()
    {
        // Maybe<TScalar> query parameters are bound by MaybeModelBinder. When binding fails
        // (e.g., raw value provided but TryCreate rejected it), the failure must be classified
        // as semantic (422), matching plain IScalarValue parameters and the Minimal API path.
        using var host = CreateMvcHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/binder-status/maybe-scalar?value=bad", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertRfc9457ProblemDetails(
            resp,
            expectedStatus: 422,
            expectedInstance: "/binder-status/maybe-scalar?value=bad");
    }

    [Fact]
    public async Task Mvc_MalformedJson_TakesPrecedenceOverQueryScalarVoFailure_Returns400()
    {
        // Mixed-failure precedence: when the body is not valid JSON AND a query scalar VO
        // ALSO fails, the wire response must be 400. Malformed bytes is a more fundamental
        // client error than any semantic value-level failure on the same request.
        using var host = CreateMvcHost();
        using var client = host.GetTestClient();

        var json = "{not valid";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("/binder-status/mixed?value=", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "malformed JSON body must dominate even when a query-bound scalar VO also fails on the same request");
    }

    [Fact]
    public async Task Mvc_ScalarVoQueryValidationFailure_WithInvalidInputMappedTo400_Returns400()
    {
        // MapError<Error.InvalidInput>(400) must reach the MVC scalar-binder validation seam,
        // not just handler-level failures. value= is empty → StatusCodeScalar.TryCreate rejects it.
        using var host = CreateMvcHost(o => o.MapError<Error.InvalidInput>(400));
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/binder-status/scalar?value=", TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertRfc9457ProblemDetails(
            resp,
            expectedStatus: 400,
            expectedInstance: "/binder-status/scalar?value=");
    }

    [Fact]
    public async Task Mvc_CompositeVoValidationFailure_WithInvalidInputMappedTo400_Returns400()
    {
        // Same override must reach the JSON-body composite-VO validation seam (MVC action filter).
        using var host = CreateMvcHost(o => o.MapError<Error.InvalidInput>(400));
        using var client = host.GetTestClient();

        var json = """{"address":{"street":"","city":"","state":""}}""";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("/binder-status/composite", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertRfc9457ProblemDetails(
            resp,
            expectedStatus: 400,
            expectedErrorKey: "address.street",
            expectedInstance: "/binder-status/composite");
    }

    [Fact]
    public async Task Mvc_MalformedJson_StaysBadRequest_EvenWhenInvalidInputRemapped()
    {
        // Remapping Error.InvalidInput to a distinctive status must not move the malformed-bytes
        // 400: syntactically invalid JSON is RFC 9110 §15.5.1, a different class than InvalidInput.
        using var host = CreateMvcHost(o => o.MapError<Error.InvalidInput>(409));
        using var client = host.GetTestClient();

        var json = "{not valid";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("/binder-status/composite", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// §8.3 — the MVC model-binding side-channel. <c>ModelStateExtensions</c> receives no
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> and <c>ModelState</c> cannot carry an
    /// <see cref="Error.InvalidInput"/>, so without a request-scoped collector a binder-produced
    /// violation reaches the wire as a string only. The binder is also the sole place in the
    /// pipeline that knows the binding source, so losing it here is what would force the
    /// projection to guess.
    /// </summary>
    [Fact]
    public async Task Mvc_query_bound_scalar_failure_emits_a_field_violation_located_in_the_query()
    {
        using var host = CreateMvcHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/binder-status/scalar?value=", TestContext.Current.CancellationToken);

        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);

        doc.RootElement.TryGetProperty("fieldViolations", out var violations).Should().BeTrue(
            "a binder-produced violation must reach the wire structurally, not only as an 'errors' string");

        var first = violations.EnumerateArray().Should().ContainSingle().Subject;
        first.GetProperty("code").GetString().Should().Be("error.unspecified");
        var location = first.GetProperty("location");
        location.GetProperty("in").GetString().Should().Be(
            "query", "the binder knows the binding source and it is the only place that does");
        location.GetProperty("name").GetString().Should().Be("value");
        location.TryGetProperty("pointer", out _).Should().BeFalse(
            "a named parameter has no document for a pointer to address");
    }

    [Fact]
    public async Task Mvc_route_bound_scalar_failure_is_located_in_the_path()
    {
        using var host = CreateMvcHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/binder-status/route/bad", TestContext.Current.CancellationToken);

        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);

        var first = doc.RootElement.GetProperty("fieldViolations").EnumerateArray().Should().ContainSingle().Subject;
        first.GetProperty("location").GetProperty("in").GetString().Should().Be("path");
        first.GetProperty("location").GetProperty("name").GetString().Should().Be("value");
    }

    /// <summary>
    /// The structured violation and the <c>errors</c> map describe the same failure, so the
    /// violation must appear once. This is the de-duplication requirement in §8.3: the filter
    /// must not re-derive from <c>ModelState</c> a violation the collector already holds.
    /// </summary>
    [Fact]
    public async Task Mvc_binder_violation_is_not_double_counted_by_the_filter()
    {
        using var host = CreateMvcHost();
        using var client = host.GetTestClient();

        var resp = await client.GetAsync("/binder-status/scalar?value=", TestContext.Current.CancellationToken);

        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("fieldViolations").GetArrayLength().Should().Be(1);
    }

    private static async Task AssertRfc9457ProblemDetails(
        HttpResponseMessage response,
        int expectedStatus,
        string? expectedErrorKey = null,
        string? expectedInstance = null)
    {
        // RFC 9457 §3 mandates application/problem+json (or +xml) for the response media type.
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var bodyText = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(bodyText);
        var root = doc.RootElement;

        // RFC 9457 §3.1 fields. `type`, `title`, `status` are the canonical members; framework
        // defaults must populate them so clients can dispatch on `type` and display `title`
        // without consulting the HTTP status line.
        root.TryGetProperty("status", out var statusEl).Should().BeTrue("RFC 9457 §3.1 status member must be present");
        statusEl.GetInt32().Should().Be(expectedStatus, "the body status field must match the HTTP status line");

        root.TryGetProperty("title", out var titleEl).Should().BeTrue("RFC 9457 §3.1 title member must be present");
        titleEl.GetString().Should().NotBeNullOrEmpty("title is a short human-readable summary; default suffices");

        root.TryGetProperty("type", out var typeEl).Should().BeTrue("RFC 9457 §3.1 type member must be present");
        var typeUri = typeEl.GetString();
        typeUri.Should().NotBeNullOrEmpty("type defaults to about:blank but should never be missing");

        if (expectedInstance is not null)
        {
            // RFC 9457 §3.1 — "instance" identifies the specific occurrence of the problem.
            // Trellis emits the server-relative path+query so clients can correlate the
            // response with the request that produced it.
            root.TryGetProperty("instance", out var instanceEl).Should().BeTrue(
                "RFC 9457 §3.1 instance must be populated on Trellis-emitted ProblemDetails responses");
            instanceEl.GetString().Should().Be(expectedInstance);
        }

        if (expectedErrorKey is not null)
        {
            root.TryGetProperty("errors", out var errorsEl).Should().BeTrue();
            errorsEl.TryGetProperty(expectedErrorKey, out _).Should().BeTrue(
                $"per-leaf entry {expectedErrorKey} must be present on the validation problem");
        }
    }
}

[JsonConverter(typeof(CompositeValueObjectJsonConverter<StatusCodeAddress>))]
public sealed class StatusCodeAddress : ValueObject
{
    public string Street { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string State { get; private set; } = string.Empty;

    private StatusCodeAddress() { }

    private StatusCodeAddress(string street, string city, string state)
    {
        Street = street;
        City = city;
        State = state;
    }

    public static Result<StatusCodeAddress> TryCreate(string street, string city, string state, string? fieldName = null)
    {
        var violations = new System.Collections.Generic.List<FieldViolation>();
        if (string.IsNullOrWhiteSpace(street))
            violations.Add(new FieldViolation(InputPointer.ForProperty("street"), ValidationCodes.Unspecified) { Detail = "Street is required." });
        if (string.IsNullOrWhiteSpace(city))
            violations.Add(new FieldViolation(InputPointer.ForProperty("city"), ValidationCodes.Unspecified) { Detail = "City is required." });
        if (string.IsNullOrWhiteSpace(state))
            violations.Add(new FieldViolation(InputPointer.ForProperty("state"), ValidationCodes.Unspecified) { Detail = "State is required." });

        return violations.Count > 0
            ? Result.Fail<StatusCodeAddress>(new Error.InvalidInput(EquatableArray.Create(violations.ToArray())))
            : Result.Ok(new StatusCodeAddress(street, city, state));
    }

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Street);
        components.Add(City);
        components.Add(State);
    }
}

public sealed record StatusCodeRequest
{
    public StatusCodeAddress Address { get; init; } = null!;
}

public sealed class StatusCodeScalar : ScalarValueObject<StatusCodeScalar, string>, IScalarValue<StatusCodeScalar, string>
{
    private StatusCodeScalar(string value) : base(value) { }

    public static Result<StatusCodeScalar> TryCreate(string? value, string? fieldName = null)
    {
        var field = fieldName ?? "value";
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Fail<StatusCodeScalar>(new Error.InvalidInput(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = $"{field} is required." })));
        }

        // Sentinel that lets tests force a TryCreate rejection even when the raw input is
        // non-empty (e.g., for `Maybe<StatusCodeScalar>` parameters where empty would bind
        // as None instead of triggering validation).
        if (value == "bad")
        {
            return Result.Fail<StatusCodeScalar>(new Error.InvalidInput(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = $"{field} cannot be 'bad'." })));
        }

        return Result.Ok(new StatusCodeScalar(value));
    }
}

[ApiController]
[Route("binder-status")]
public sealed class StatusCodeController : ControllerBase
{
    [HttpPost("composite")]
    public IActionResult PostComposite([FromBody] StatusCodeRequest request) => Ok();

    [HttpGet("scalar")]
    public IActionResult GetScalar([FromQuery] StatusCodeScalar value) => Ok();

    [HttpGet("route/{value}")]
    public IActionResult GetRouteScalar([FromRoute] StatusCodeScalar value) => Ok();

    [HttpGet("maybe-scalar")]
    public IActionResult GetMaybeScalar([FromQuery] Maybe<StatusCodeScalar> value) => Ok();

    [HttpPost("mixed")]
    public IActionResult PostMixed(
        [FromQuery] StatusCodeScalar value,
        [FromBody] StatusCodeRequest request) => Ok();
}