namespace Trellis.Asp.Tests;

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis;
using Xunit;

public sealed class BoundErrorMetadataTests(BoundErrorMetadataFixture fixture)
    : IClassFixture<BoundErrorMetadataFixture>
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task BindingFailure_RootMetadataAndRules_PreservesSingleOrCombinesNeutrally(
        bool mvc, bool rulesOnly, bool multipleFailures)
    {
        var path = mvc ? "/bound-error-metadata/mvc" : "/bound-error-metadata/minimal";
        var code = rulesOnly ? "RULE" : "BAD";
        var other = multipleFailures ? "RULE" : "ORD-456";

        using var valid = await fixture.Client.GetAsync(
            $"{path}?code=ORD-123&other=ORD-456", TestContext.Current.CancellationToken);
        valid.StatusCode.Should().Be(HttpStatusCode.OK);
        valid.Headers.GetValues("X-Handler-Executed").Should().Equal("true");

        using var response = await fixture.Client.GetAsync(
            $"{path}?code={code}&other={other}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Headers.Contains("X-Handler-Executed").Should().BeFalse();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        problem.GetProperty("kind").GetString().Should().Be("unprocessable-content");
        problem.GetProperty("code").GetString().Should().Be(
            multipleFailures ? ValidationCodes.Unspecified : MetadataCode.RootCode);
        if (multipleFailures)
            problem.TryGetProperty("detail", out var detail).Should().BeFalse();
        else
            problem.GetProperty("detail").GetString().Should().Be(MetadataCode.RootDetail);

        var errors = problem.GetProperty("errors");
        errors.EnumerateObject().Should().HaveCount(multipleFailures ? 2 : 1);
        errors.GetProperty("code").EnumerateArray().Should().ContainSingle()
            .Which.GetString().Should().Be(rulesOnly ? MetadataCode.RootDetail : MetadataCode.FieldDetail);
        if (multipleFailures)
            errors.GetProperty("other")[0].GetString().Should().Be(MetadataCode.RootDetail);

        if (rulesOnly)
            problem.TryGetProperty("fieldViolations", out _).Should().BeFalse();
        else
        {
            var field = problem.GetProperty("fieldViolations").EnumerateArray().Should().ContainSingle().Which;
            field.GetProperty("code").GetString().Should().Be("order.code-prefix");
            field.GetProperty("args").GetProperty("prefix").GetString().Should().Be("ORD-");
            field.GetProperty("location").GetProperty("in").GetString().Should().Be("query");
            field.GetProperty("location").GetProperty("name").GetString().Should().Be("code");
        }

        var rules = problem.GetProperty("ruleViolations").EnumerateArray().ToArray();
        rules.Should().HaveCount(multipleFailures ? 2 : 1);
        foreach (var rule in rules)
        {
            rule.GetProperty("code").GetString().Should().Be("order.code-policy");
            rule.GetProperty("args").GetProperty("limit").GetInt32().Should().Be(3);
            var location = rule.GetProperty("locations").EnumerateArray().Should().ContainSingle().Which;
            location.GetProperty("in").GetString().Should().Be("query");
            location.GetProperty("name").GetString().Should().BeOneOf("code", "other");
        }

        rules.Select(rule => rule.GetProperty("locations")[0].GetProperty("name").GetString())
            .Should().Equal(multipleFailures ? ["code", "other"] : ["code"]);
        problem.TryGetProperty("cause", out _).Should().BeFalse();
        body.Should().NotContain(MetadataCode.CauseDetail);
    }

    public sealed class MetadataCode : ScalarValueObject<MetadataCode, string>, IScalarValue<MetadataCode, string>
    {
        public const string RootCode = "order.invalid-code";
        public const string RootDetail = "Order code was rejected.";
        public const string FieldDetail = "Code must start with ORD-.";
        public const string CauseDetail = "Internal policy context.";

        private MetadataCode(string value) : base(value) { }

        public static Result<MetadataCode> TryCreate(string? value, string? fieldName = null) =>
            value is not null && value.StartsWith("ORD-", StringComparison.Ordinal)
                ? Result.Ok(new MetadataCode(value))
                : Result.Fail<MetadataCode>(CreateError(fieldName, value == "RULE"));

        public static bool TryParse(string? value, [MaybeNullWhen(false)] out MetadataCode result) =>
            TryCreate(value).TryGetValue(out result);

        public static Error.InvalidInput CreateError(string? fieldName = null, bool rulesOnly = false)
        {
            var field = InputPointer.ForProperty(fieldName ?? "value");
            var error = Error.InvalidInput.ForField(
                "order.code-prefix", field, ValidationArgs.Of("prefix", "ORD-"), FieldDetail);
            return error with
            {
                Fields = rulesOnly ? default : error.Fields,
                Rules = EquatableArray.Create(new RuleViolation(
                    "order.code-policy", EquatableArray.Create(field), ValidationArgs.Of("limit", 3))),
                Code = RootCode,
                Detail = RootDetail,
                Cause = new Error.Forbidden("orders.restricted") { Detail = CauseDetail },
            };
        }
    }
}

public sealed class BoundErrorMetadataFixture : IAsyncLifetime
{
    private WebApplication _app = null!;

    public HttpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(BoundErrorMetadataController).Assembly);
        builder.Services.AddTrellisAspWithScalarValidation();
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        _app = builder.Build();
        _app.UseScalarValueValidation();
        _app.MapControllers();
        _app.MapGet("/bound-error-metadata/minimal",
            ([FromQuery] BoundErrorMetadataTests.MetadataCode code,
             [FromQuery] BoundErrorMetadataTests.MetadataCode other,
             HttpContext context) =>
            {
                context.Response.Headers["X-Handler-Executed"] = "true";
                return Results.Ok(new[] { code.Value, other.Value });
            }).WithScalarValueValidation();
        await _app.StartAsync();
        Client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}

[ApiController]
[Route("bound-error-metadata/mvc")]
public sealed class BoundErrorMetadataController : ControllerBase
{
    [HttpGet]
    public IActionResult Get(
        [FromQuery] BoundErrorMetadataTests.MetadataCode code,
        [FromQuery] BoundErrorMetadataTests.MetadataCode other)
    {
        Response.Headers["X-Handler-Executed"] = "true";
        return Ok(new[] { code.Value, other.Value });
    }
}