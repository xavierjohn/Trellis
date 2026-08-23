namespace Trellis.Asp.Tests;

using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis;

/// <summary>
/// Pins how <see cref="ProducesAttribute"/> interacts with the two response shapes
/// <c>Trellis.Asp</c> ships, because the two behave in opposite ways and the difference
/// decides what the planned <c>[Produces]</c> analyzer is allowed to say.
/// <para>
/// <see cref="ProducesAttribute"/> is a result filter that rewrites
/// <see cref="ObjectResult.ContentTypes"/> and touches nothing else. A Trellis failure is an
/// <c>IResult</c> that writes its own media type when it executes, and
/// <c>AsActionResult&lt;T&gt;()</c> wraps it in a plain <see cref="ActionResult"/> rather than an
/// <see cref="ObjectResult"/> -- so the filter cannot see it. MVC's own automatic
/// model-validation response *is* an <see cref="ObjectResult"/>, so it is fully exposed.
/// </para>
/// </summary>
public sealed class ProducesAttributeContentTypeTests
{
    private static IHost CreateHost()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddProblemDetails();
                    s.AddControllers().AddApplicationPart(typeof(ProducesNoneController).Assembly);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return builder.Start();
    }

    private static async Task<string?> MediaTypeOfGetAsync(string path)
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        var resp = await client.GetAsync(path, TestContext.Current.CancellationToken);
        return resp.Content.Headers.ContentType?.MediaType;
    }

    private static async Task<string?> MediaTypeOfEmptyPostAsync(string path)
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        var resp = await client.PostAsJsonAsync(path, new { }, TestContext.Current.CancellationToken);
        return resp.Content.Headers.ContentType?.MediaType;
    }

    public static TheoryData<string> AllProducesVariants =>
    [
        "produces-none",
        "produces-json",
        "produces-append",
        "produces-prepend",
    ];

    [Theory]
    [MemberData(nameof(AllProducesVariants))]
    public async Task Trellis_failure_via_AsActionResult_keeps_problem_json_under_every_Produces_variant(string prefix) =>
        (await MediaTypeOfGetAsync($"/{prefix}/domain-422"))
            .Should().Be("application/problem+json",
                "AsActionResult wraps the IResult in a plain ActionResult, which the Produces result filter cannot rewrite");

    [Theory]
    [MemberData(nameof(AllProducesVariants))]
    public async Task Trellis_success_via_AsActionResult_keeps_json_under_every_Produces_variant(string prefix) =>
        (await MediaTypeOfGetAsync($"/{prefix}/ok"))
            .Should().Be("application/json");

    [Fact]
    public async Task Automatic_model_validation_is_problem_json_when_no_Produces_is_applied() =>
        (await MediaTypeOfEmptyPostAsync("/produces-none/binder"))
            .Should().Be("application/problem+json");

    [Fact]
    public async Task Automatic_model_validation_is_clobbered_by_json_only_Produces() =>
        (await MediaTypeOfEmptyPostAsync("/produces-json/binder"))
            .Should().Be("application/json", "this is the RFC 9457 regression the analyzer must catch");

    [Fact]
    public async Task Appending_problem_json_does_not_repair_automatic_model_validation() =>
        (await MediaTypeOfEmptyPostAsync("/produces-append/binder"))
            .Should().Be("application/json",
                "selection follows list order, so problem+json is inert anywhere but first -- an analyzer keyed on 'omits problem+json' would go green here");

    [Fact]
    public async Task Prepending_problem_json_repairs_automatic_model_validation() =>
        (await MediaTypeOfEmptyPostAsync("/produces-prepend/binder"))
            .Should().Be("application/problem+json");

    [Fact]
    public async Task Prepending_problem_json_rewrites_plain_ObjectResult_success_responses() =>
        (await MediaTypeOfGetAsync("/produces-prepend/plain-ok"))
            .Should().Be("application/problem+json",
                "a plain ObjectResult 200 negotiates on list order too, so prepending corrupts success responses");

    [Fact]
    public async Task Plain_ObjectResult_success_is_json_when_no_Produces_is_applied() =>
        (await MediaTypeOfGetAsync("/produces-none/plain-ok"))
            .Should().Be("application/json");

    private static IHost CreateScalarValidationHost(System.Action<MvcOptions>? configureMvc = null)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddProblemDetails();
                    s.AddTrellisAspWithScalarValidation(_ => { });
                    s.AddControllers(o => configureMvc?.Invoke(o))
                        .AddApplicationPart(typeof(ProducesNoneController).Assembly);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return builder.Start();
    }

    private static async Task<HttpResponseMessage> ScalarFailureAsync(
        string prefix,
        string? accept = null,
        System.Action<MvcOptions>? configureMvc = null)
    {
        using var host = CreateScalarValidationHost(configureMvc);
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{prefix}/scalar?value=");
        if (accept is not null)
            request.Headers.Add("Accept", accept);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string?> ScalarFailureMediaTypeAsync(string prefix)
    {
        var resp = await ScalarFailureAsync(prefix);
        return resp.Content.Headers.ContentType?.MediaType;
    }

    [Fact]
    public async Task Trellis_scalar_validation_filter_is_problem_json_when_no_Produces_is_applied() =>
        (await ScalarFailureMediaTypeAsync("noproduces-scalar"))
            .Should().Be("application/problem+json");

    [Fact]
    public async Task Trellis_scalar_validation_filter_keeps_problem_json_under_Produces() =>
        (await ScalarFailureMediaTypeAsync("produces-scalar"))
            .Should().Be("application/problem+json",
                "a Trellis-owned RFC 9457 response must keep its media type even when the controller carries [Produces]");

    private static async Task<string?> RouteScalarFailureMediaTypeAsync(string prefix)
    {
        using var host = CreateScalarValidationHost();
        using var client = host.GetTestClient();
        // A route parameter binds through a different value provider than a query parameter,
        // so it is asserted separately rather than assumed to share the query seam's fate.
        // "bad" is StatusCodeScalar's sentinel: a non-empty value TryCreate still rejects.
        var resp = await client.GetAsync($"/{prefix}/route/bad", TestContext.Current.CancellationToken);
        return resp.Content.Headers.ContentType?.MediaType;
    }

    [Theory]
    [InlineData("noproduces-scalar")]
    [InlineData("produces-scalar")]
    [InlineData("produces-action-scalar")]
    public async Task Trellis_route_parameter_validation_failure_keeps_problem_json(string prefix) =>
        (await RouteScalarFailureMediaTypeAsync(prefix))
            .Should().Be("application/problem+json");

    [Fact]
    public async Task Trellis_scalar_validation_filter_keeps_problem_json_under_action_level_Produces() =>
        (await ScalarFailureMediaTypeAsync("produces-action-scalar"))
            .Should().Be("application/problem+json",
                "[Produces] is a result filter wherever it is declared, so action-level must behave as class-level does");

    [Fact]
    public async Task Action_level_Produces_clobbers_the_stock_seam_exactly_as_class_level_does() =>
        (await MediaTypeOfEmptyPostAsync("/produces-action-scalar/binder"))
            .Should().Be("application/json",
                "[Produces] is the same result filter wherever it is declared, so action-level clobbers an unowned ObjectResult exactly as class-level does");

    // When ScalarValueValidationFilter is registered, it takes over EVERY invalid ModelState --
    // not just value-object failures but plain DataAnnotations ones too (its final
    // `else if (!ModelState.IsValid)` branch). So in a Trellis-configured app that seam is
    // Trellis-owned and the fix covers it; the clobbering above is stock MVC with no filter.
    [Theory]
    [InlineData("produces-action-scalar")]
    [InlineData("produces-scalar")]
    public async Task DataAnnotations_failure_keeps_problem_json_once_the_scalar_filter_is_registered(string prefix) =>
        (await PostMediaTypeAsync(prefix, "binder", """{}"""))
            .Should().Be("application/problem+json");

    private static async Task<string?> PostMediaTypeAsync(string prefix, string path, string json) =>
        (await PostAsync(prefix, path, json)).Content.Headers.ContentType?.MediaType;

    private static async Task<HttpResponseMessage> PostAsync(string prefix, string path, string json)
    {
        using var host = CreateScalarValidationHost();
        using var client = host.GetTestClient();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return await client.PostAsync($"/{prefix}/{path}", content, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("noproduces-scalar")]
    [InlineData("produces-scalar")]
    [InlineData("produces-action-scalar")]
    public async Task Trellis_composite_validation_failure_keeps_problem_json(string prefix) =>
        (await PostMediaTypeAsync(prefix, "composite", """{"address":{"street":"","city":"","state":""}}"""))
            .Should().Be("application/problem+json");

    [Theory]
    [InlineData("noproduces-scalar")]
    [InlineData("produces-scalar")]
    public async Task Trellis_malformed_json_failure_keeps_problem_json(string prefix) =>
        (await PostMediaTypeAsync(prefix, "composite", "{ not json"))
            .Should().Be("application/problem+json");

    // A body that is well-formed JSON but fails to CONVERT a plain, non-value-object property is
    // a third shape, distinct from both rows above: no value object rejects anything, so
    // `addedScalarValueFailure` stays false and the response is produced by the filter's final
    // `else if (!ModelState.IsValid)` branch. Deserialization aborts before the action's model is
    // built, so the parameter binds null and the problem carries no `fieldViolations` -- which is
    // why it looks, from outside, like a response the filter never touched.
    private const string BodyFailingConversion = """{"visitedOn":"not-a-date"}""";

    [Fact]
    public async Task Body_conversion_failure_is_clobbered_by_Produces_when_no_filter_is_registered()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        using var content = new StringContent(BodyFailingConversion, System.Text.Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/produces-json/bad-date", content, TestContext.Current.CancellationToken);

        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/json",
            "the stock response is an ObjectResult, so [Produces] rewrites it -- this row is what makes the Trellis row below non-vacuous");
    }

    [Theory]
    [InlineData("noproduces-scalar")]
    [InlineData("produces-scalar")]
    [InlineData("produces-action-scalar")]
    public async Task Body_conversion_failure_keeps_problem_json_once_the_scalar_filter_is_registered(string prefix)
    {
        var resp = await PostAsync(prefix, "bad-date", BodyFailingConversion);

        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a body that never parsed had nothing semantically rejected, so 400 wins over the value-object 422");

        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().NotContain("fieldViolations",
            "nothing was projected, so absence of fieldViolations does NOT mean the filter was bypassed");
    }

    // Negotiation must behave exactly as it did when this was a bare ObjectResult. The wrapper
    // declares problem+json and problem+xml because those are precisely the two media types MVC
    // infers for a ProblemDetails value with an empty content-type list; this expectation was
    // confirmed against that baseline by letting MVC infer the list again.
    [Theory]
    [InlineData("noproduces-scalar")]
    [InlineData("produces-scalar")]
    public async Task Trellis_scalar_failure_is_still_written_for_plain_json_accept(string prefix)
    {
        var resp = await ScalarFailureAsync(
            prefix,
            accept: "application/json",
            configureMvc: o => o.ReturnHttpNotAcceptable = true);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "declaring the problem media types explicitly must not turn a validation failure into a 406");
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}

public sealed class ThingRequest
{
    [Required]
    public string? Name { get; set; }
}

/// <summary>
/// Carries a plain, non-value-object property whose JSON conversion can fail, so a bad value
/// aborts body deserialization without any value object being consulted.
/// </summary>
public sealed class DateRequest
{
    public DateOnly VisitedOn { get; set; }
}

#pragma warning disable CA1822
public abstract class ProducesProbeControllerBase : ControllerBase
{
    public record T(int Id);

    [HttpGet("domain-422")]
    public Task<ActionResult<T>> Domain422()
    {
        var fields = EquatableArray.Create(
            new FieldViolation(new InputPointer("/email"), "format", null, "must be email"));
        return Task.FromResult(
            Result.Fail<T>(new Error.InvalidInput(fields))
                .ToHttpResponse(t => t)
                .AsActionResult<T>());
    }

    [HttpGet("ok")]
    public Task<ActionResult<T>> Ok200() =>
        Task.FromResult(
            Result.Ok(new T(1))
                .ToHttpResponse(t => t)
                .AsActionResult<T>());

    [HttpGet("plain-ok")]
    public ActionResult<T> PlainOk() => Ok(new T(1));

    [HttpPost("binder")]
    public ActionResult<T> Binder([FromBody] ThingRequest request) => Ok(new T(request.Name!.Length));

    [HttpPost("bad-date")]
    public ActionResult<T> BadDate([FromBody] DateRequest request) => Ok(new T(request.VisitedOn.Day));
}

[ApiController]
[Route("produces-none")]
public sealed class ProducesNoneController : ProducesProbeControllerBase;

[ApiController]
[Route("produces-json")]
[Produces("application/json")]
public sealed class ProducesJsonController : ProducesProbeControllerBase;

[ApiController]
[Route("produces-append")]
[Produces("application/json", "application/problem+json")]
public sealed class ProducesAppendController : ProducesProbeControllerBase;

[ApiController]
[Route("produces-prepend")]
[Produces("application/problem+json", "application/json")]
public sealed class ProducesPrependController : ProducesProbeControllerBase;

[ApiController]
[Route("noproduces-scalar")]
public sealed class NoProducesScalarController : ControllerBase
{
    [HttpGet("scalar")]
    public IActionResult GetScalar([FromQuery] StatusCodeScalar value) => Ok();

    [HttpGet("route/{value}")]
    public IActionResult GetRouteScalar([FromRoute] StatusCodeScalar value) => Ok();

    [HttpPost("composite")]
    public IActionResult PostComposite([FromBody] StatusCodeRequest request) => Ok();

    [HttpPost("bad-date")]
    public IActionResult PostBadDate([FromBody] DateRequest request) => Ok();
}

[ApiController]
[Route("produces-scalar")]
[Produces("application/json")]
public sealed class ProducesScalarController : ControllerBase
{
    [HttpGet("scalar")]
    public IActionResult GetScalar([FromQuery] StatusCodeScalar value) => Ok();

    [HttpGet("route/{value}")]
    public IActionResult GetRouteScalar([FromRoute] StatusCodeScalar value) => Ok();

    [HttpPost("composite")]
    public IActionResult PostComposite([FromBody] StatusCodeRequest request) => Ok();

    [HttpPost("bad-date")]
    public IActionResult PostBadDate([FromBody] DateRequest request) => Ok();

    [HttpPost("binder")]
    public IActionResult Binder([FromBody] ThingRequest request) => Ok(new { request.Name!.Length });
}

// Class-level [Produces] is what both this framework and the reporting consumer had measured;
// action-level was an open question on both sides. It resolves the same way: ProducesAttribute
// is a result filter regardless of where it is declared, so the seam's fate is unchanged.
[ApiController]
[Route("produces-action-scalar")]
public sealed class ProducesActionScalarController : ControllerBase
{
    [HttpGet("scalar")]
    [Produces("application/json")]
    public IActionResult GetScalar([FromQuery] StatusCodeScalar value) => Ok();

    [HttpGet("route/{value}")]
    [Produces("application/json")]
    public IActionResult GetRouteScalar([FromRoute] StatusCodeScalar value) => Ok();

    [HttpPost("composite")]
    [Produces("application/json")]
    public IActionResult PostComposite([FromBody] StatusCodeRequest request) => Ok();

    [HttpPost("bad-date")]
    [Produces("application/json")]
    public IActionResult PostBadDate([FromBody] DateRequest request) => Ok();

    // Stock DataAnnotations seam, which Trellis does not own. Asserting that this IS clobbered
    // at action level is what establishes action-level and class-level behave alike; the
    // Trellis rows above cannot show it, since they are immune either way.
    [HttpPost("binder")]
    [Produces("application/json")]
    public IActionResult Binder([FromBody] ThingRequest request) => Ok(new { request.Name!.Length });
}
#pragma warning restore CA1822
