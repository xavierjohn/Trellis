namespace Trellis.Asp.Tests;

using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;
using Xunit;

public class ResponseFailureWriterInputOriginTests
{
    [Fact]
    public async Task UnlocatedFieldViolation_UnderBodyDeclaration_ProjectsAsBody()
    {
        using var body = await WriteAsync(
            NewContext(InputLocation.Body),
            Error.InvalidInput.ForField("amount", "validation.range"));

        LocationOf(body).Should().Be("body");
        PointerOf(body).Should().Be("/amount");
    }

    [Fact]
    public async Task UnlocatedFieldViolation_UnderQueryDeclaration_ProjectsAsQuery()
    {
        using var body = await WriteAsync(
            NewContext(InputLocation.Query),
            Error.InvalidInput.ForField("cursor", "cursor.malformed"));

        LocationOf(body).Should().Be("query");
        NameOf(body).Should().Be(
            "cursor",
            "a query location is addressed by parameter name, the same shape the model binder emits");
    }

    [Fact]
    public async Task UnlocatedFieldViolation_OnUndeclaredEndpoint_StaysUnknown()
    {
        using var body = await WriteAsync(
            NewContext(declared: null),
            Error.InvalidInput.ForField("amount", "validation.range"));

        LocationOf(body).Should().Be("unknown");
    }

    [Fact]
    public async Task UnspecifiedDeclaration_OptsOutOfPromotion()
    {
        using var body = await WriteAsync(
            NewContext(InputLocation.Unspecified),
            Error.InvalidInput.ForField("amount", "validation.range"));

        LocationOf(body).Should().Be("unknown");
    }

    [Fact]
    public async Task LocatedViolation_IsNeverRelabelled()
    {
        using var body = await WriteAsync(
            NewContext(InputLocation.Body),
            Error.InvalidInput.ForField(InputPointer.ForQuery("cursor"), "cursor.malformed"));

        LocationOf(body).Should().Be("query");
    }

    [Fact]
    public async Task NestedPointer_UnderQueryDeclaration_IsDeclined()
    {
        using var body = await WriteAsync(
            NewContext(InputLocation.Query),
            Error.InvalidInput.ForField(InputPointer.ForProperty("/lines/0/amount"), "validation.range"));

        LocationOf(body).Should().Be("unknown", "no query string can carry a nested document pointer");
        PointerOf(body).Should().Be("/lines/0/amount");
    }

    [Fact]
    public async Task NestedPointer_UnderBodyDeclaration_IsPromoted()
    {
        using var body = await WriteAsync(
            NewContext(InputLocation.Body),
            Error.InvalidInput.ForField(InputPointer.ForProperty("/lines/0/amount"), "validation.range"));

        LocationOf(body).Should().Be("body");
        PointerOf(body).Should().Be("/lines/0/amount");
    }

    [Fact]
    public async Task RuleViolationPointers_ArePromoted()
    {
        var error = new Error.InvalidInput(EquatableArray<FieldViolation>.Empty, EquatableArray.Create(
            new RuleViolation("rule.crossField", EquatableArray.Create(
                InputPointer.ForProperty("start"),
                InputPointer.ForProperty("end")))));

        using var body = await WriteAsync(NewContext(InputLocation.Body), error);

        var pointers = body.RootElement.GetProperty("ruleViolations")[0].GetProperty("locations");
        pointers[0].GetProperty("in").GetString().Should().Be("body");
        pointers[1].GetProperty("in").GetString().Should().Be("body");
    }

    [Fact]
    public async Task AggregateChildViolation_IsPromoted()
    {
        var error = new Error.Aggregate(EquatableArray.Create<Error>(
            Error.InvalidInput.ForField("amount", "validation.range")));

        using var body = await WriteAsync(NewContext(InputLocation.Body), error);

        body.RootElement
            .GetProperty("problems")[0]
            .GetProperty("fieldViolations")[0]
            .GetProperty("location")
            .GetProperty("in")
            .GetString()
            .Should().Be("body");
    }

    [Fact]
    public async Task PromotedAggregate_KeepsItsOwnDetail()
    {
        var error = new Error.Aggregate(EquatableArray.Create<Error>(
            Error.InvalidInput.ForField("amount", "validation.range")))
        {
            Detail = "The batch was rejected.",
        };

        using var body = await WriteAsync(NewContext(InputLocation.Body), error);

        body.RootElement.GetProperty("detail").GetString().Should().Be("The batch was rejected.");
    }

    [Fact]
    public void PromotedAggregate_KeepsItsCauseChain()
    {
        var cause = new Error.NotFound(new ResourceRef("Account", "a1"));
        var error = new Error.Aggregate(EquatableArray.Create<Error>(
            Error.InvalidInput.ForField("amount", "validation.range")))
        {
            Cause = cause,
        };

        var promoted = InputOriginPromotion.Apply(NewContext(InputLocation.Body), error);

        promoted.Should().NotBeSameAs(error);
        promoted.Cause.Should().Be(cause);
    }

    [Fact]
    public async Task UnchangedViolations_SurviveTheRebuild_BeforeAndAfterAPromotedOne()
    {
        var error = new Error.InvalidInput(EquatableArray.Create(
            new FieldViolation(InputPointer.ForQuery("first"), "a"),
            new FieldViolation(InputPointer.ForProperty("middle"), "b"),
            new FieldViolation(InputPointer.ForHeader("last"), "c")));

        using var body = await WriteAsync(NewContext(InputLocation.Body), error);

        var violations = body.RootElement.GetProperty("fieldViolations");
        violations[0].GetProperty("location").GetProperty("in").GetString().Should().Be("query");
        violations[1].GetProperty("location").GetProperty("in").GetString().Should().Be("body");
        violations[2].GetProperty("location").GetProperty("in").GetString().Should().Be("header");
        violations.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void PromotedInvalidInput_KeepsItsDetailAndCause()
    {
        var cause = new Error.NotFound(new ResourceRef("Account", "a1"));
        var error = Error.InvalidInput.ForField("amount", "validation.range") with
        {
            Detail = "The request was rejected.",
            Cause = cause,
        };

        var promoted = InputOriginPromotion.Apply(NewContext(InputLocation.Body), error);

        promoted.Should().NotBeSameAs(error);
        promoted.Detail.Should().Be("The request was rejected.");
        promoted.Cause.Should().Be(cause);
    }

    [Theory]
    [InlineData(InputLocation.Body)]
    [InlineData(InputLocation.Header)]
    [InlineData(InputLocation.Path)]
    public void QueryDeclaration_NeverRelabelsAnAlreadyLocatedPointer(InputLocation existing)
    {
        var located = InputPointer.ForProperty("field") with { In = existing };

        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContext(InputLocation.Query),
            Error.InvalidInput.ForField(located, "some.code"));

        promoted.Fields.Items[0].Field.In.Should().Be(existing);
    }

    [Fact]
    public void QueryDeclaration_PromotesAnEscapedSingleMemberName()
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContext(InputLocation.Query),
            Error.InvalidInput.ForField(InputPointer.ForQuery("a/b") with { In = InputLocation.Unspecified }, "code"));

        promoted.Fields.Items[0].Field.In.Should().Be(
            InputLocation.Query,
            "'/' inside a member name is escaped as ~1, so the pointer still names exactly one member");
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public void QueryDeclaration_DeclinesAPointerThatNamesNoSingleMember(string path)
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContext(InputLocation.Query),
            Error.InvalidInput.ForField(new InputPointer(path), "code"));

        promoted.Fields.Items[0].Field.In.Should().Be(InputLocation.Unspecified);
    }

    [Fact]
    public void ANearerUnspecifiedDeclaration_OverridesAnEnclosingOne()
    {
        var context = NewContext(
            new InputOriginAttribute(InputLocation.Body),
            new InputOriginAttribute(InputLocation.Unspecified));

        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            context,
            Error.InvalidInput.ForField("amount", "validation.range"));

        promoted.Fields.Items[0].Field.In.Should().Be(
            InputLocation.Unspecified,
            "an action must be able to opt out of its controller's declaration");
    }

    [Fact]
    public async Task TheMvcShapedErrorsMap_SeesThePromotedPointer()
    {
        using var body = await WriteAsync(
            NewContext(InputLocation.Query),
            Error.InvalidInput.ForField("cursor", "cursor.malformed"));

        body.RootElement.GetProperty("errors").TryGetProperty("cursor", out _)
            .Should().BeTrue("every projection must read the same promoted error");
    }

    [Fact]
    public async Task NonValidationError_IsUnaffected()
    {
        using var body = await WriteAsync(
            NewContext(InputLocation.Body),
            new Error.NotFound(new ResourceRef("Account", "a1")),
            statusCode: 404);

        body.RootElement.GetProperty("status").GetInt32().Should().Be(404);
    }

    [Fact]
    public void TheNearestDeclarationWins()
    {
        var context = NewContext(new InputOriginAttribute(InputLocation.Body), new InputOriginAttribute(InputLocation.Query));

        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            context,
            Error.InvalidInput.ForField("cursor", "cursor.malformed"));

        promoted.Fields.Items[0].Field.In.Should().Be(
            InputLocation.Query,
            "an action's own declaration is appended after its controller's and must override it");
    }

    [Theory]
    [InlineData(InputLocation.Path)]
    [InlineData(InputLocation.Header)]
    [InlineData((InputLocation)99)]
    public void UnsupportedLocation_IsRejected(InputLocation location) =>
        FluentActions.Invoking(() => new InputOriginAttribute(location))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void WithInputOrigin_AddsTheDeclarationToTheEndpoint()
    {
        var builder = new StubConventionBuilder();

        builder.WithInputOrigin(InputLocation.Query);

        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), 0);
        foreach (var convention in builder.Conventions) convention(endpoint);

        endpoint.Metadata.OfType<InputOriginAttribute>().Single().Location
            .Should().Be(InputLocation.Query);
    }

    private static string? LocationOf(JsonDocument body) =>
        body.RootElement.GetProperty("fieldViolations")[0].GetProperty("location").GetProperty("in").GetString();

    private static string? PointerOf(JsonDocument body) =>
        body.RootElement.GetProperty("fieldViolations")[0].GetProperty("location").GetProperty("pointer").GetString();

    private static string? NameOf(JsonDocument body) =>
        body.RootElement.GetProperty("fieldViolations")[0].GetProperty("location").GetProperty("name").GetString();

    private static DefaultHttpContext NewContext(InputLocation? declared) =>
        declared is null ? NewContext() : NewContext(new InputOriginAttribute(declared.Value));

    private static DefaultHttpContext NewContext(params InputOriginAttribute[] declarations)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        context.Features.Set<IEndpointFeature>(new StubEndpointFeature(
            new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(declarations), "test")));
        return context;
    }

    private static async Task<JsonDocument> WriteAsync(HttpContext context, Error error, int statusCode = 422)
    {
        await ResponseFailureWriter.WriteAsync(context, error, statusCode);
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    private sealed class StubEndpointFeature(Endpoint endpoint) : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; } = endpoint;
    }

    private sealed class StubConventionBuilder : IEndpointConventionBuilder
    {
        public List<Action<EndpointBuilder>> Conventions { get; } = [];

        public void Add(Action<EndpointBuilder> convention) => Conventions.Add(convention);
    }
}
