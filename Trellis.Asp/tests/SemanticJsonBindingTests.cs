namespace Trellis.Asp.Tests;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis.Asp.Validation;
using Trellis.Primitives;

[Collection(nameof(AotPathTrackingTests))]
public sealed class SemanticJsonBindingTests
{
    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    [InlineData("Production", true)]
    public async Task Bind_CollectedSemanticFailures_Returns422WithoutExecutingTheHandler(
        string environment, bool strictNullability)
    {
        var calls = 0;
        await using var app = await CreateAppAsync(environment, strictNullability,
            () => calls++);
        using var client = app.GetTestClient();
        using var content = new StringContent(
            """{"state":"Unknown","quantity":0,"amount":{"amount":-1,"currency":"USD"}}""",
            Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/semantic", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        calls.Should().Be(0);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        problem.RootElement.GetProperty("fieldViolations").EnumerateArray()
            .Select(v => v.GetProperty("location").GetProperty("pointer").GetString())
            .Should().BeEquivalentTo(["/state", "/quantity", "/amount/amount"]);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Bind_RootSemanticFailureFollowedByMalformedJson_Preserves400(string environment)
    {
        var calls = 0;
        await using var app = await CreateAppAsync(environment, true, () => calls++);
        using var client = app.GetTestClient();
        using var content = new StringContent("\"Unknown\" invalid", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/scalar", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        calls.Should().Be(0);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        problem.RootElement.GetProperty("kind").GetString().Should().Be("bad-request");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Bind_SemanticFailureFollowedByMalformedJson_Returns400WithoutExecutingTheHandler(
        string environment)
    {
        var calls = 0;
        await using var app = await CreateAppAsync(environment, false, () => calls++);
        using var client = app.GetTestClient();
        using var content = new StringContent("""{"state":"Unknown","quantity":""",
            Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/semantic", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        calls.Should().Be(0);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        problem.RootElement.GetProperty("kind").GetString().Should().Be("bad-request");
    }

    [Theory]
    [InlineData("Development", """{"state":null,"quantity":null,"amount":null}""")]
    [InlineData("Production", """{"state":null,"quantity":null,"amount":null}""")]
    [InlineData("Development", "{}")]
    [InlineData("Production", "{}")]
    public async Task Bind_RequiredValueObjectsAreAbsent_Returns422WithoutExecutingTheHandler(
        string environment, string json)
    {
        var calls = 0;
        await using var app = await CreateAppAsync(environment, true, () => calls++);
        using var client = app.GetTestClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/semantic", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        calls.Should().Be(0);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        problem.RootElement.GetProperty("fieldViolations").EnumerateArray()
            .Select(v => v.GetProperty("location").GetProperty("pointer").GetString())
            .Should().BeEquivalentTo(["/state", "/quantity", "/amount"]);
    }

    [Theory]
    [InlineData("Development", """{"state":"Draft","quantity":1,"amount":{"amount":1,"currency":"USD"},"name":null}""")]
    [InlineData("Production", """{"state":"Draft","quantity":1,"amount":{"amount":1,"currency":"USD"},"name":null}""")]
    [InlineData("Development", """{"state":"Draft","quantity":1,"amount":{"amount":1,"currency":"USD"}}""")]
    [InlineData("Production", """{"state":"Draft","quantity":1,"amount":{"amount":1,"currency":"USD"}}""")]
    public async Task Bind_NonValueObjectRequiredViolation_PreservesFramework400(string environment, string json)
    {
        var calls = 0;
        await using var app = await CreateAppAsync(environment, true, () => calls++);
        using var client = app.GetTestClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/strict", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task Bind_StandaloneMissingRequiredValueObjects_RejectsInsteadOfReturningPlaceholders()
    {
        await using var app = await CreateAppAsync("Production", false, () => { });
        var options = app.Services.GetRequiredService<
            IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;

        var deserialize = () => JsonSerializer.Deserialize<SemanticRequest>("{}", options);

        deserialize.Should().Throw<TrellisJsonValidationException>();
    }

    [Theory]
    [InlineData("Development", "/scalar", "\"Unknown\"")]
    [InlineData("Production", "/scalar", "\"Unknown\"")]
    [InlineData("Development", "/scalar", "null")]
    [InlineData("Production", "/scalar", "null")]
    [InlineData("Development", "/composite", """{"amount":-1,"currency":"USD"}""")]
    [InlineData("Production", "/composite", """{"amount":-1,"currency":"USD"}""")]
    public async Task Bind_InvalidRootValue_Returns422WithoutExecutingTheHandler(
        string environment, string route, string json)
    {
        var calls = 0;
        await using var app = await CreateAppAsync(environment, true, () => calls++);
        using var client = app.GetTestClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(route, content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        calls.Should().Be(0);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        problem.RootElement.GetProperty("fieldViolations").EnumerateArray()
            .Select(v => v.GetProperty("location").GetProperty("pointer").GetString())
            .Should().Equal([route == "/scalar" ? string.Empty : "/amount"]);
    }

    [Theory]
    [InlineData("Development", """{"amount":{"amount":-1,"currency":"USD"}}""")]
    [InlineData("Production", """{"amount":{"amount":-1,"currency":"USD"}}""")]
    [InlineData("Development", """{"amount":null}""")]
    [InlineData("Production", """{"amount":null}""")]
    [InlineData("Development", "{}")]
    [InlineData("Production", "{}")]
    public async Task Bind_CallbackDto_InvalidRequiredValue_Returns422BeforeTheCallback(
        string environment, string json)
    {
        var calls = 0;
        await using var app = await CreateAppAsync(environment, true, () => calls++);
        using var client = app.GetTestClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/callback", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task Bind_CallbackDto_ValidRequiredValue_PreservesTheCallback()
    {
        var calls = 0;
        await using var app = await CreateAppAsync("Production", true, () => calls++);
        using var client = app.GetTestClient();
        using var content = new StringContent(
            """{"amount":{"amount":3,"currency":"USD"}}""", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/callback", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        calls.Should().Be(1);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetDecimal().Should().Be(3m);
    }

    [Fact]
    public async Task Bind_CallbackDto_StandaloneMissingValue_RejectsBeforeTheCallback()
    {
        await using var app = await CreateAppAsync("Production", false, () => { });
        var options = app.Services.GetRequiredService<
            IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;

        var deserialize = () => JsonSerializer.Deserialize<CallbackRequest>("{}", options);

        deserialize.Should().Throw<TrellisJsonValidationException>();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Bind_CallbackDto_MissingNestedRequiredValue_SuppressesAncestorCallbacks(string environment)
    {
        var calls = 0;
        await using var app = await CreateAppAsync(environment, true, () => calls++);
        using var client = app.GetTestClient();
        using var content = new StringContent("""{"nested":{}}""", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(
            "/nested-callback", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task Bind_CallbackDto_UnrelatedSiblingFailure_PreservesValidNestedCallbacks()
    {
        await using var app = await CreateAppAsync("Production", true, () => { });
        var options = app.Services.GetRequiredService<
            IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        using var scope = ValidationErrorsContext.BeginScope();

        var request = JsonSerializer.Deserialize<CallbackSiblingRequest>(
            """{"quantity":0,"nested":{"amount":{"amount":3,"currency":"USD"}}}""", options);

        request!.Nested.ObservedAmount.Should().Be(3m);
        ValidationErrorsContext.GetUnprocessableContent()!.Fields.Items
            .Should().ContainSingle(v => v.Field.Path == "/quantity");
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    [InlineData("Production", true)]
    public async Task Bind_CallbackDto_OutputOnlyValue_IsInitializedByTheCallback(
        string environment, bool generatedMetadata)
    {
        var calls = 0;
        await using var app = await CreateAppAsync(environment, true, () => calls++, generatedMetadata);
        using var client = app.GetTestClient();
        using var content = new StringContent("""{"rawQuantity":1}""", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(
            "/derived-callback", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        calls.Should().Be(1);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("1");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bind_CallbackDto_OutputOnlyValue_StandalonePreservesInitialization(bool generatedMetadata)
    {
        await using var app = await CreateAppAsync("Production", true, () => { }, generatedMetadata);
        var options = app.Services.GetRequiredService<
            IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;

        var request = JsonSerializer.Deserialize<DerivedCallbackRequest>("""{"rawQuantity":1}""", options);

        request!.Quantity.Value.Should().Be(1m);
    }

    [Theory]
    [InlineData(false, "{}")]
    [InlineData(true, "{}")]
    [InlineData(false, """{"quantity":null,"amount":null}""")]
    [InlineData(true, """{"quantity":null,"amount":null}""")]
    public async Task Bind_GetterOnlyConstructorInputs_AbsentValues_RemainRequired(
        bool generatedMetadata, string json)
    {
        await using var app = await CreateAppAsync("Production", true, () => { }, generatedMetadata);
        var options = app.Services.GetRequiredService<
            IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        using var scope = ValidationErrorsContext.BeginScope();

        _ = JsonSerializer.Deserialize<ConstructorBoundRequest>(json, options);

        ValidationErrorsContext.GetUnprocessableContent()!.Fields.Items
            .Select(v => v.Field.Path).Should().BeEquivalentTo(["/quantity", "/amount"]);
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    [InlineData("Production", true)]
    public async Task Bind_RuleOnlyCompositeFailure_DoesNotReportTheSuppliedValueAsMissing(
        string environment, bool omitRequired)
    {
        var calls = 0;
        await using var app = await CreateAppAsync(environment, true, () => calls++);
        using var client = app.GetTestClient();
        var json = omitRequired
            ? """{"range":{"start":2,"end":1}}"""
            : """{"range":{"start":2,"end":1},"required":{"amount":1,"currency":"USD"}}""";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/rule", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        calls.Should().Be(0);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        problem.RootElement.GetProperty("ruleViolations").EnumerateArray()
            .Select(v => v.GetProperty("code").GetString()).Should().Equal(["range.order"]);
        var fields = problem.RootElement.TryGetProperty("fieldViolations", out var fieldViolations)
            ? fieldViolations.EnumerateArray().ToArray()
            : [];
        fields.Select(v => v.GetProperty("location").GetProperty("pointer").GetString())
            .Should().Equal(omitRequired ? ["/required"] : []);
        fields.Select(v => v.GetProperty("code").GetString())
            .Should().Equal(omitRequired ? ["value.not-null"] : []);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Bind_OptionalRootSemanticFailure_InvalidQuery_Preserves400(string environment)
    {
        var calls = 0;
        await using var app = await CreateAppAsync(environment, true, () => calls++);
        using var client = app.GetTestClient();
        using var content = new StringContent(
            """{"amount":-1,"currency":"USD"}""", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(
            "/optional-composite?count=invalid", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        calls.Should().Be(0);
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    [InlineData("Production", true)]
    public async Task Bind_RequiredRootBody_InvalidQuery_PreservesBindingPrecedence(
        string environment, bool invalidBody)
    {
        var calls = 0;
        await using var app = await CreateAppAsync(environment, true, () => calls++);
        using var client = app.GetTestClient();
        var amount = invalidBody ? -1 : 3;
        using var content = new StringContent(
            $$"""{"amount":{{amount}},"currency":"USD"}""", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(
            "/required-composite?count=invalid", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(invalidBody
            ? HttpStatusCode.UnprocessableEntity
            : HttpStatusCode.BadRequest);
        calls.Should().Be(0);
    }

    private static async Task<WebApplication> CreateAppAsync(
        string environment, bool strictNullability, Action handler, bool generatedMetadata = false)
    {
        ScalarValuePathTracking.RegisterScalar<DcInt, int>();
        ScalarValuePathTracking.RegisterProperty<DcInt>();
        ScalarValuePathTracking.RegisterProperty<DcOrderState>();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        if (generatedMetadata)
            builder.Services.ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, CallbackJsonContext.Default));
        builder.Services.AddTrellisAspWithScalarValidation();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.RespectNullableAnnotations = strictNullability;
            options.SerializerOptions.RespectRequiredConstructorParameters = strictNullability;
        });
        var app = builder.Build();
        // TestServer propagates binding exceptions that Kestrel would render with their HTTP status.
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (BadHttpRequestException exception)
            {
                context.Response.StatusCode = exception.StatusCode;
            }
        });
        app.UseScalarValueValidation();
        app.MapPost("/semantic", ([FromBody] SemanticRequest request) =>
        {
            handler();
            return Results.Ok();
        }).WithScalarValueValidation();
        app.MapPost("/strict", ([FromBody] StrictRequest request) =>
        {
            handler();
            return Results.Ok();
        }).WithScalarValueValidation();
        app.MapPost("/scalar", ([FromBody] DcOrderState state) =>
        {
            handler();
            return Results.Ok();
        }).WithScalarValueValidation();
        app.MapPost("/composite", ([FromBody] Money amount) =>
        {
            handler();
            return Results.Ok();
        }).WithScalarValueValidation();
        app.MapPost("/optional-composite", ([FromBody] Money? amount, [FromQuery] int count) =>
        {
            handler();
            return Results.Ok();
        }).WithScalarValueValidation();
        app.MapPost("/required-composite", ([FromBody] Money amount, [FromQuery] int count) =>
        {
            handler();
            return Results.Ok();
        }).WithScalarValueValidation();
        app.MapPost("/callback", ([FromBody] CallbackRequest request) =>
        {
            handler();
            return Results.Ok(request.ObservedAmount);
        }).WithScalarValueValidation();
        app.MapPost("/nested-callback", ([FromBody] NestedCallbackRequest request) =>
        {
            handler();
            return Results.Ok(request.ObservedAmount);
        }).WithScalarValueValidation();
        app.MapPost("/derived-callback", ([FromBody] DerivedCallbackRequest request) =>
        {
            handler();
            return Results.Ok(request.Quantity.Value);
        }).WithScalarValueValidation();
        app.MapPost("/rule", ([FromBody] RuleRequest request) =>
        {
            handler();
            return Results.Ok();
        }).WithScalarValueValidation();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    public sealed record SemanticRequest(DcOrderState State, DcInt Quantity, Money Amount);
    public sealed record StrictRequest(DcOrderState State, DcInt Quantity, Money Amount, string Name);
    public sealed record RuleRequest(RuleOnlyRange Range, Money Required);
    public sealed record CallbackSiblingRequest(DcInt Quantity, CallbackRequest Nested);

    public sealed class ConstructorBoundRequest(MonetaryAmount quantity, Money amount)
    {
        public MonetaryAmount Quantity { get; } = quantity;
        public Money Amount { get; } = amount;
    }

    public sealed class DerivedCallbackRequest(int rawQuantity) : IJsonOnDeserialized
    {
        private MonetaryAmount? _quantity;
        public int RawQuantity { get; } = rawQuantity;
        public MonetaryAmount Quantity => _quantity!;

        void IJsonOnDeserialized.OnDeserialized() => _quantity = MonetaryAmount.Create(RawQuantity);
    }

    public sealed record NestedCallbackRequest(CallbackRequest Nested) : IJsonOnDeserialized
    {
        [JsonIgnore]
        public decimal ObservedAmount { get; private set; }

        void IJsonOnDeserialized.OnDeserialized() => ObservedAmount = Nested.Amount.Amount;
    }

    public sealed record CallbackRequest(Money Amount) : IJsonOnDeserialized
    {
        [JsonIgnore]
        public decimal ObservedAmount { get; private set; }

        void IJsonOnDeserialized.OnDeserialized() => ObservedAmount = Amount.Amount;
    }

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<RuleOnlyRange>))]
    public sealed class RuleOnlyRange : ValueObject
    {
        public int Start { get; }
        public int End { get; }

        private RuleOnlyRange(int start, int end)
        {
            Start = start;
            End = end;
        }

        public static Result<RuleOnlyRange> TryCreate(int start, int end) =>
            start <= end
                ? Result.Ok(new RuleOnlyRange(start, end))
                : Result.Fail<RuleOnlyRange>(
                    Error.InvalidInput.ForRule("range.order", detail: "Start must not exceed end."));

        protected override void GetEqualityComponents(ref EqualityComponents components)
        {
            components.Add(Start);
            components.Add(End);
        }
    }

}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SemanticJsonBindingTests.DerivedCallbackRequest))]
[JsonSerializable(typeof(SemanticJsonBindingTests.ConstructorBoundRequest))]
internal partial class CallbackJsonContext : JsonSerializerContext;