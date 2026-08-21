namespace Trellis.Asp.Tests;

using System;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
    public async Task AViolationNamingARouteParameter_IsStampedPath_NotTheDeclaredBody()
    {
        // POST /employee/{employeeId} with a body: the domain rejects the id from the URL, so the
        // declaration covering this endpoint must not claim the caller's body was at fault.
        using var body = await WriteAsync(
            NewContext(InputLocation.Body, "employee/{employeeId}"),
            Error.InvalidInput.ForField("employeeId", "employee.unknown"));

        LocationOf(body).Should().Be("path");
        NameOf(body).Should().Be("employeeId");
    }

    [Fact]
    public async Task AMixedOriginFailure_LocatesEachViolationSeparately()
    {
        var error = new Error.InvalidInput(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("employeeId"), "employee.unknown"),
            new FieldViolation(InputPointer.ForProperty("salary"), "validation.range")));

        using var body = await WriteAsync(NewContext(InputLocation.Body, "employee/{employeeId}"), error);

        var violations = body.RootElement.GetProperty("fieldViolations");
        violations[0].GetProperty("location").GetProperty("in").GetString().Should().Be("path");
        violations[1].GetProperty("location").GetProperty("in").GetString().Should().Be("body");
    }

    [Fact]
    public void TheRouteGuardOutranksAQueryDeclarationToo()
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContext(InputLocation.Query, "employee/{employeeId}"),
            Error.InvalidInput.ForField("employeeId", "employee.unknown"));

        promoted.Fields.Items[0].Field.In.Should().Be(InputLocation.Path);
    }

    [Fact]
    public void TheRouteGuardMatchesCaseInsensitively()
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContext(InputLocation.Body, "employee/{employeeId}"),
            Error.InvalidInput.ForField("EMPLOYEEID", "employee.unknown"));

        promoted.Fields.Items[0].Field.In.Should().Be(
            InputLocation.Path,
            "routing matches route parameter names case-insensitively");
    }

    [Fact]
    public void ANestedPointerWhoseFirstSegmentMatchesARouteParameter_IsNotStampedPath()
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContext(InputLocation.Body, "employee/{employeeId}"),
            Error.InvalidInput.ForField(InputPointer.ForProperty("/employeeId/0/name"), "code"));

        promoted.Fields.Items[0].Field.In.Should().Be(
            InputLocation.Body,
            "a document pointer addresses the body even when its first segment shares a route parameter's name");
    }

    [Fact]
    public void AnEndpointWithoutARoutePattern_StillHonoursItsDeclaration()
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContext(InputLocation.Body),
            Error.InvalidInput.ForField("amount", "validation.range"));

        promoted.Fields.Items[0].Field.In.Should().Be(InputLocation.Body);
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
    public void ANonValidationError_NeverConsultsTheBindingMap()
    {
        var context = NewContextWithBinding(declared: null, "accounts/{id}/deposit", bindsBody: true);
        var provider = (StubApiDescriptionProvider)context.RequestServices
            .GetRequiredService<IApiDescriptionGroupCollectionProvider>();

        InputOriginPromotion.Apply(context, new Error.NotFound(new ResourceRef("Account", "a1")));

        provider.Reads.Should().Be(
            0,
            "discovering a binding map walks every API description, which a 404 should not pay for");
    }

    [Fact]
    public void AHandlerMappedToTwoMethodsOnOneRoute_ResolvesTheMethodBeingServed()
    {
        var context = NewContextWithSharedRoutes(
            servedRoute: "items/{id}",
            servedMethod: "POST",
            described:
            [
                ("items/{id}", "GET", "note", BindingSource.Query),
                ("items/{id}", "POST", "request", BindingSource.Body),
            ]);

        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            context,
            Error.InvalidInput.ForField("note", "note.invalid"));

        promoted.Fields.Items[0].Field.In.Should().Be(
            InputLocation.Body,
            "the same template is mapped twice, so the HTTP method decides which binding map applies");
    }

    [Fact]
    public void TwoDescriptionsSharingRouteAndMethod_DeriveNothing()
    {
        var context = NewContextWithSharedRoutes(
            servedRoute: "items/{id}",
            servedMethod: "POST",
            described:
            [
                ("items/{id}", "POST", "note", BindingSource.Query),
                ("items/{id}", "POST", "request", BindingSource.Body),
            ]);

        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            context,
            Error.InvalidInput.ForField("note", "note.invalid"));

        promoted.Fields.Items[0].Field.In.Should().Be(
            InputLocation.Unspecified,
            "route and method cannot separate the two, so no evidence is derived rather than a guess");
    }

    [Fact]
    public void AFormBoundEndpoint_CountsAsBindingABody()
    {
        var context = NewContextWithSharedRoutes(
            servedRoute: "items",
            servedMethod: "POST",
            described: [("items", "POST", "upload", BindingSource.Form)]);

        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            context,
            Error.InvalidInput.ForField("amount", "validation.range"));

        promoted.Fields.Items[0].Field.In.Should().Be(
            InputLocation.Body,
            "a form post carries its values in the request body");
    }

    private static DefaultHttpContext NewContextWithSharedRoutes(
        string servedRoute,
        string servedMethod,
        (string Route, string Method, string Parameter, BindingSource Source)[] described)
    {
        var handler = typeof(ResponseFailureWriterInputOriginTests)
            .GetMethod(nameof(DescribedHandler), BindingFlags.NonPublic | BindingFlags.Static)!;

        var descriptions = new ApiDescription[described.Length];
        for (var i = 0; i < described.Length; i++)
        {
            var description = new ApiDescription
            {
                RelativePath = described[i].Route,
                HttpMethod = described[i].Method,
                ActionDescriptor = new ActionDescriptor { EndpointMetadata = [handler] },
            };

            description.ParameterDescriptions.Add(new ApiParameterDescription
            {
                Name = described[i].Parameter,
                Source = described[i].Source,
            });

            descriptions[i] = description;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IApiDescriptionGroupCollectionProvider>(new StubApiDescriptionProvider(descriptions));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        context.Features.Set<IEndpointFeature>(new StubEndpointFeature(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(servedRoute),
            0,
            new EndpointMetadataCollection(new HttpMethodMetadata([servedMethod]), handler),
            "test")));
        return context;
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

    [Fact]
    public async Task AViolationNamingAQueryParameter_IsStampedQuery_NotTheDeclaredBody()
    {
        using var body = await WriteAsync(
            NewContext(InputLocation.Body, "accounts", "cursor"),
            Error.InvalidInput.ForField("cursor", "cursor.malformed"));

        LocationOf(body).Should().Be("query");
        NameOf(body).Should().Be("cursor");
    }

    [Fact]
    public void ARouteParameterOutranksAQueryParameterOfTheSameName()
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContext(InputLocation.Body, "employee/{id}", "id"),
            Error.InvalidInput.ForField("id", "employee.unknown"));

        promoted.Fields.Items[0].Field.In.Should().Be(InputLocation.Path);
    }

    [Fact]
    public void TheQueryEvidenceMatchesCaseInsensitively()
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContext(InputLocation.Body, "accounts", "cursor"),
            Error.InvalidInput.ForField("CURSOR", "cursor.malformed"));

        promoted.Fields.Items[0].Field.In.Should().Be(InputLocation.Query);
    }

    [Fact]
    public void ANameTheUrlDoesNotAccountFor_FallsToTheDeclaredResidual()
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContext(InputLocation.Body, "accounts", "cursor"),
            Error.InvalidInput.ForField("amount", "validation.range"));

        promoted.Fields.Items[0].Field.In.Should().Be(InputLocation.Body);
    }

    [Fact]
    public void ANestedPointerNamingAQueryParameter_IsNotStampedQuery()
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContext(InputLocation.Body, "accounts", "cursor"),
            Error.InvalidInput.ForField(InputPointer.ForProperty("/cursor/0/value"), "validation.range"));

        promoted.Fields.Items[0].Field.In.Should().Be(InputLocation.Body);
    }

    [Fact]
    public void WithoutApiExplorer_TheDeclaredResidualStillApplies()
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContext(InputLocation.Body),
            Error.InvalidInput.ForField("cursor", "cursor.malformed"));

        promoted.Fields.Items[0].Field.In.Should().Be(InputLocation.Body);
    }

    [Fact]
    public void AHandlerMappedToTwoRoutes_ResolvesTheRouteBeingServed()
    {
        var context = NewContextWithSharedHandler(
            servedRoute: "items/search",
            other: ("items/{id}", "id", BindingSource.Path),
            served: ("items/search", "id", BindingSource.Query));

        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            context,
            Error.InvalidInput.ForField("id", "id.malformed"));

        promoted.Fields.Items[0].Field.In.Should().Be(
            InputLocation.Query,
            "the other route binds id from the path, but this endpoint binds it from the query string");
    }

    [Fact]
    public void AHandlerMappedToTwoRoutes_FallsToTheResidualWhenNeitherRouteMatches()
    {
        var context = NewContextWithSharedHandler(
            servedRoute: "items/unlisted",
            other: ("items/browse", "id", BindingSource.Query),
            served: ("items/search", "id", BindingSource.Query));

        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            context,
            Error.InvalidInput.ForField("id", "id.malformed"));

        promoted.Fields.Items[0].Field.In.Should().Be(
            InputLocation.Body,
            "an ambiguous handler yields no evidence rather than another route's binding map");
    }

    [Fact]
    public async Task AnEndpointThatBindsABody_LocatesItsResidualWithoutDeclaringAnything()
    {
        using var body = await WriteAsync(
            NewContextWithBinding(declared: null, "accounts/{id}/deposit", bindsBody: true),
            Error.InvalidInput.ForField("amount", "validation.range"));

        LocationOf(body).Should().Be("body");
        PointerOf(body).Should().Be("/amount");
    }

    [Fact]
    public void AnEndpointThatBindsNoBody_LeavesItsResidualUnknown()
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContextWithBinding(declared: null, "accounts/{id}/close", bindsBody: false),
            Error.InvalidInput.ForField("reason", "validation.required"));

        promoted.Fields.Items[0].Field.In.Should().Be(
            InputLocation.Unspecified,
            "there is no body to attribute the residual to, so unknown remains the honest answer");
    }

    [Fact]
    public void TheUrlIsStillEvidenceOnAnEndpointThatDeclaredNothing()
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContextWithBinding(declared: null, "accounts/{id}", bindsBody: false, "cursor"),
            new Error.InvalidInput(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty("id"), "account.unknown"),
                new FieldViolation(InputPointer.ForProperty("cursor"), "cursor.malformed"))));

        promoted.Fields.Items[0].Field.In.Should().Be(InputLocation.Path);
        promoted.Fields.Items[1].Field.In.Should().Be(InputLocation.Query);
    }

    [Fact]
    public void AnExplicitUnspecified_OptsOutOfTheDerivedBodyResidual()
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContextWithBinding(InputLocation.Unspecified, "accounts/{id}/deposit", bindsBody: true),
            Error.InvalidInput.ForField("amount", "validation.range"));

        promoted.Fields.Items[0].Field.In.Should().Be(
            InputLocation.Unspecified,
            "an explicit declaration overrides what the binding map would otherwise imply");
    }

    [Fact]
    public void AHeaderBoundNameIsNotSweptIntoTheBodyResidual()
    {
        var promoted = (Error.InvalidInput)InputOriginPromotion.Apply(
            NewContextWithHeaderAndBody("accounts", "tenantId"),
            new Error.InvalidInput(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty("tenantId"), "tenant.unknown"),
                new FieldViolation(InputPointer.ForProperty("amount"), "validation.range"))));

        promoted.Fields.Items[0].Field.In.Should().Be(
            InputLocation.Header,
            "the endpoint binds tenantId from a header, so the body residual must not claim it");
        promoted.Fields.Items[1].Field.In.Should().Be(InputLocation.Body);
    }

    private static DefaultHttpContext NewContext(InputLocation? declared) =>
        declared is null ? NewContext() : NewContext(new InputOriginAttribute(declared.Value));

    private static DefaultHttpContext NewContextWithHeaderAndBody(string routePattern, string headerParameter)
    {
        var handler = typeof(ResponseFailureWriterInputOriginTests)
            .GetMethod(nameof(DescribedHandler), BindingFlags.NonPublic | BindingFlags.Static)!;

        var description = new ApiDescription
        {
            RelativePath = routePattern,
            ActionDescriptor = new ActionDescriptor { EndpointMetadata = [handler] },
        };

        description.ParameterDescriptions.Add(
            new ApiParameterDescription { Name = headerParameter, Source = BindingSource.Header });
        description.ParameterDescriptions.Add(
            new ApiParameterDescription { Name = "request", Source = BindingSource.Body });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IApiDescriptionGroupCollectionProvider>(new StubApiDescriptionProvider(description));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        context.Features.Set<IEndpointFeature>(new StubEndpointFeature(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(routePattern),
            0,
            new EndpointMetadataCollection(handler),
            "test")));
        return context;
    }

    private static DefaultHttpContext NewContextWithSharedHandler(
        string servedRoute,
        (string Route, string Parameter, BindingSource Source) other,
        (string Route, string Parameter, BindingSource Source) served)
    {
        var handler = typeof(ResponseFailureWriterInputOriginTests)
            .GetMethod(nameof(DescribedHandler), BindingFlags.NonPublic | BindingFlags.Static)!;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IApiDescriptionGroupCollectionProvider>(
            new StubApiDescriptionProvider(Describe(other), Describe(served)));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        context.Features.Set<IEndpointFeature>(new StubEndpointFeature(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(servedRoute),
            0,
            new EndpointMetadataCollection(new InputOriginAttribute(InputLocation.Body), handler),
            "test")));
        return context;

        ApiDescription Describe((string Route, string Parameter, BindingSource Source) route)
        {
            var description = new ApiDescription
            {
                RelativePath = route.Route,
                ActionDescriptor = new ActionDescriptor { EndpointMetadata = [handler] },
            };

            description.ParameterDescriptions.Add(
                new ApiParameterDescription { Name = route.Parameter, Source = route.Source });

            return description;
        }
    }

    private static DefaultHttpContext NewContext(InputLocation declared, string routePattern) =>
        NewContext(declared, routePattern, []);

    private static DefaultHttpContext NewContext(
        InputLocation declared,
        string routePattern,
        params string[] queryParameters) =>
        NewContextWithBinding(declared, routePattern, bindsBody: false, queryParameters);

    private static DefaultHttpContext NewContextWithBinding(
        InputLocation? declared,
        string routePattern,
        bool bindsBody,
        params string[] queryParameters)
    {
        var handler = typeof(ResponseFailureWriterInputOriginTests)
            .GetMethod(nameof(DescribedHandler), BindingFlags.NonPublic | BindingFlags.Static)!;

        var description = new ApiDescription
        {
            RelativePath = routePattern,
            ActionDescriptor = new ActionDescriptor { EndpointMetadata = [handler] },
        };

        foreach (var name in queryParameters)
            description.ParameterDescriptions.Add(new ApiParameterDescription { Name = name, Source = BindingSource.Query });

        if (bindsBody)
            description.ParameterDescriptions.Add(new ApiParameterDescription { Name = "request", Source = BindingSource.Body });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IApiDescriptionGroupCollectionProvider>(new StubApiDescriptionProvider(description));

        var metadata = declared is null
            ? new EndpointMetadataCollection(handler)
            : new EndpointMetadataCollection(new InputOriginAttribute(declared.Value), handler);

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        context.Features.Set<IEndpointFeature>(new StubEndpointFeature(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(routePattern),
            0,
            metadata,
            "test")));
        return context;
    }

    private static void DescribedHandler()
    {
    }

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

    private sealed class StubApiDescriptionProvider : IApiDescriptionGroupCollectionProvider
    {
        private readonly ApiDescriptionGroupCollection groups;

        public StubApiDescriptionProvider(params ApiDescription[] descriptions) =>
            this.groups = new([new ApiDescriptionGroup("test", descriptions)], 1);

        public int Reads { get; private set; }

        public ApiDescriptionGroupCollection ApiDescriptionGroups
        {
            get
            {
                this.Reads++;
                return this.groups;
            }
        }
    }

    private sealed class StubConventionBuilder : IEndpointConventionBuilder
    {
        public List<Action<EndpointBuilder>> Conventions { get; } = [];

        public void Add(Action<EndpointBuilder> convention) => Conventions.Add(convention);
    }
}
