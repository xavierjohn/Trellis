namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;
using Trellis.Testing;

/// <summary>
/// Tests for the <see cref="AuthFailureExposurePolicy"/> translation logic in
/// <see cref="ResourceAuthorizationBehavior{TMessage, TResource, TResponse}"/>.
/// </summary>
public sealed class ResourceAuthorizationBehaviorExposurePolicyTests
{
    [Fact]
    public async Task Handle_DefaultPolicyPropagate_ForbiddenAuthFailurePassesThrough()
    {
        var resource = new HiddenResource("res-1", "owner-1", "kind-public");
        var behavior = CreateBehavior<HideExistenceCommand>(actorId: "other-user", resource, new ResourceAuthorizationOptions());
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
    }

    [Fact]
    public async Task Handle_HideExistenceConfigured_ForbiddenTranslatedToNotFoundWithResourceRef()
    {
        var resource = new HiddenResource("res-1", "owner-1", "kind-public");
        var options = new ResourceAuthorizationOptions().HideExistence<HiddenResource>();
        var behavior = CreateBehavior<HideExistenceCommand>(actorId: "other-user", resource, options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("HiddenResource");
        notFound.Resource.Id.Should().Be("res-1");
    }

    [Fact]
    public async Task Handle_HideExistenceConfigured_LoadFailureForbiddenTranslatedToNotFound()
    {
        // A loader returning Result.Fail<TestResource>(Forbidden) — e.g. a remote ACL — must
        // also flow through the translation, not just authorize-failures. The spec calls this
        // out explicitly: "Apply policy ... to both load-failure and authorize-failure paths."
        var loaderForbidden = new Error.Forbidden("acl.downstream-denied");
        var options = new ResourceAuthorizationOptions().HideExistence<HiddenResource>();
        var behavior = CreateBehaviorWithLoaderError(actorId: "owner-1", loaderForbidden, options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("HiddenResource");
        notFound.Resource.Id.Should().Be("res-1");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Handle_HideAsNotFound_LoadFailureAbsent_UsesCanonicalPublicError(bool useDefaultPolicy, bool gone)
    {
        var privateCode = gone ? "storage.row-gone" : "storage.row-missing";
        var privateDetail = gone ? "Internal row database-id was purged." : "Internal row database-id was not found.";
        var resourceRef = ResourceRef.For("InternalRow", "database-id");
        Error loaderError = gone ? new Error.Gone(resourceRef) : new Error.NotFound(resourceRef);
        loaderError = loaderError with
        {
            Code = privateCode,
            Detail = privateDetail,
            Cause = new Error.Forbidden("storage.partition-hidden"),
        };
        var options = useDefaultPolicy
            ? new ResourceAuthorizationOptions { DefaultExposurePolicy = AuthFailureExposurePolicy.HideAsNotFound }
            : new ResourceAuthorizationOptions().HideExistence<HiddenResource>();
        var behavior = CreateBehaviorWithLoaderError(actorId: "owner-1", loaderError, options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Should().NotBeSameAs(loaderError);
        notFound.Resource.Should().Be(ResourceRef.For<HiddenResource>("res-1"));
        notFound.Code.Should().Be("error.unspecified");
        notFound.Detail.Should().BeNull();
        notFound.Cause.Should().BeNull();
        loaderError.Code.Should().Be(privateCode);
        loaderError.Detail.Should().Be(privateDetail);
    }

    [Fact]
    public async Task Handle_Propagate_LoadFailureNotFound_PreservesOriginalError()
    {
        var original = Error.NotFound.For<HiddenResource>(
            "storage.row-missing", id: "res-1", detail: "Detailed loader failure.");
        var behavior = CreateBehaviorWithLoaderError("owner-1", original, new ResourceAuthorizationOptions());

        var result = await InvokeHide(behavior, new HideExistenceCommand("res-1"));

        result.UnwrapError().Should().BeSameAs(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_Propagate_LoadFailureGone_PreservesOriginalError(bool overrideDefaultHide)
    {
        var original = Error.Gone.For<HiddenResource>(
            "storage.row-gone", id: "res-1", detail: "Detailed tombstone failure.");
        var options = new ResourceAuthorizationOptions
        {
            DefaultExposurePolicy = overrideDefaultHide
                ? AuthFailureExposurePolicy.HideAsNotFound
                : AuthFailureExposurePolicy.Propagate,
        };
        if (overrideDefaultHide)
            options.Propagate<HiddenResource>();
        var behavior = CreateBehaviorWithLoaderError("owner-1", original, options);

        var result = await InvokeHide(behavior, new HideExistenceCommand("res-1"));

        result.UnwrapError().Should().BeSameAs(original);
    }

    [Fact]
    public async Task Handle_HideAsNotFound_HandlerGone_PreservesOriginalError()
    {
        var original = Error.Gone.For<HiddenResource>("resource.removed", id: "res-1");
        var behavior = CreateBehavior<HideExistenceCommand>("owner-1",
            new HiddenResource("res-1", "owner-1", "kind"),
            new ResourceAuthorizationOptions().HideExistence<HiddenResource>());
        var (next, tracker) = NextDelegate.TrackingAsync<HideExistenceCommand, Result<string>>(Result.Fail<string>(original));

        var result = await behavior.HandleWithContext(
            new HideExistenceCommand("res-1"), next, TestContext.Current.CancellationToken);

        tracker.WasInvoked.Should().BeTrue();
        result.UnwrapError().Should().BeSameAs(original);
    }

    [Fact]
    public async Task Handle_HideAsNotFound_LoadFailureUnexpected_PreservesOriginalError()
    {
        var original = new Error.Unexpected("storage.failed", "fault-42");
        var behavior = CreateBehaviorWithLoaderError(
            "owner-1", original, new ResourceAuthorizationOptions().HideExistence<HiddenResource>());

        var result = await InvokeHide(behavior, new HideExistenceCommand("res-1"));

        result.UnwrapError().Should().BeSameAs(original);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("gone")]
    [InlineData("denied")]
    [InlineData("anonymous")]
    [InlineData("loader-forbidden")]
    [InlineData("loader-authentication")]
    [InlineData("authorize-not-found")]
    [InlineData("authorize-gone")]
    public async Task Handle_PublicMetadata_FailureSources_UseSamePublicError(string source)
    {
        var options = new ResourceAuthorizationOptions()
            .HideExistence<HiddenResource>("resource.not-found", "Resource not found.");
        var resource = new HiddenResource("res-1", "owner-1", "kind");
        Error? authorizationError = source switch
        {
            "authorize-not-found" => Error.NotFound.For<UnrelatedResource>("policy.hidden", detail: "Private policy detail."),
            "authorize-gone" => Error.Gone.For<UnrelatedResource>("policy.removed", detail: "Private policy detail."),
            _ => null,
        };
        var command = new HideExistenceCommand("res-1", authorizationError);
        var behavior = source switch
        {
            "missing" => CreateBehaviorWithLoaderError("other-user",
                Error.NotFound.For<UnrelatedResource>("storage.missing", id: "internal-id", detail: "Private loader detail."), options),
            "gone" => CreateBehaviorWithLoaderError("other-user",
                Error.Gone.For<UnrelatedResource>("storage.gone", id: "internal-id", detail: "Private tombstone detail."), options),
            "loader-forbidden" => CreateBehaviorWithLoaderError("other-user", new Error.Forbidden("loader.denied"), options),
            "loader-authentication" => CreateBehaviorWithLoaderError("other-user", new Error.AuthenticationRequired("PrivateScheme"), options),
            "anonymous" => CreateBehavior<HideExistenceCommand>(null, resource, options),
            _ => CreateBehavior<HideExistenceCommand>("other-user", resource, options),
        };

        var result = await InvokeHide(behavior, command);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Should().Be(ResourceRef.For<HiddenResource>("res-1"));
        notFound.Code.Should().Be("resource.not-found");
        notFound.Detail.Should().Be("Resource not found.");
        notFound.Cause.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Handle_PublicDetail_OptionalCode_UsesStandardUnspecifiedCode(string? code)
    {
        var options = new ResourceAuthorizationOptions().HideExistence<HiddenResource>(code, "Resource not found.");
        var behavior = CreateBehaviorWithLoaderError("owner-1",
            Error.NotFound.For<HiddenResource>("private.missing", id: "res-1"), options);

        var result = await InvokeHide(behavior, new HideExistenceCommand("res-1"));

        result.UnwrapError().Code.Should().Be("error.unspecified");
        result.UnwrapError().Detail.Should().Be("Resource not found.");
    }

    [Theory]
    [InlineData("not-found")]
    [InlineData("gone")]
    [InlineData("forbidden")]
    [InlineData("authentication-required")]
    public async Task Handle_HideAsNotFound_PrivateDiagnostics_RetainOriginalKindAndCode(string kind)
    {
        Error original = kind switch
        {
            "not-found" => Error.NotFound.For<UnrelatedResource>("private.missing", id: "internal-id"),
            "gone" => Error.Gone.For<UnrelatedResource>("private.gone", id: "internal-id"),
            "forbidden" => new Error.Forbidden("private.denied"),
            _ => new Error.AuthenticationRequired { Code = "private.unauthenticated" },
        };
        var logger = new CapturingLogger<ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>>();
        var options = new ResourceAuthorizationOptions()
            .HideExistence<HiddenResource>("resource.not-found", "Resource not found.");
        var behavior = CreateBehaviorWithLoaderError("owner-1", original, options, logger);

        var result = await InvokeHide(behavior, new HideExistenceCommand("res-1"));

        result.UnwrapError().Code.Should().Be("resource.not-found");
        logger.Entries.Should().ContainSingle();
        var entry = logger.Entries.Single();
        entry.Level.Should().Be(LogLevel.Information);
        entry.EventId.Id.Should().Be(1);
        entry.EventId.Name.Should().Be("ExistenceHidden");
        var fields = entry.State.Should()
            .BeAssignableTo<IEnumerable<KeyValuePair<string, object?>>>().Which;
        fields.Should().Contain(new KeyValuePair<string, object?>("OriginalKind", original.Kind));
        fields.Should().Contain(new KeyValuePair<string, object?>("OriginalCode", original.Code));
        fields.Should().Contain(new KeyValuePair<string, object?>("PublicResourceType", nameof(HiddenResource)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_ProjectionPublicMetadata_AbsentResource_UsesConfiguredPublicError(bool gone)
    {
        var options = new ResourceAuthorizationOptions()
            .HideExistence<AuthorizationProjection, PublicAggregate>("public.not-found", "Public aggregate not found.");
        Error original = gone
            ? Error.Gone.For<AuthorizationProjection>("projection.gone", id: "internal-id", detail: "Private tombstone detail.")
            : Error.NotFound.For<AuthorizationProjection>("projection.missing", id: "internal-id", detail: "Private projection detail.");
        var behavior = CreateProjectionBehavior("owner-1", Result.Fail<AuthorizationProjection>(original), options);

        var result = await InvokeProjection(behavior, new ProjectionAuthorizedCommand("public-id-42"));

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Should().Be(ResourceRef.For<PublicAggregate>("public-id-42"));
        notFound.Code.Should().Be("public.not-found");
        notFound.Detail.Should().Be("Public aggregate not found.");
        notFound.Cause.Should().BeNull();
    }

    [Theory]
    [InlineData("service.degraded")]
    public async Task Handle_HideExistenceConfigured_LoadFailureUnavailablePassesThrough(string reason)
    {
        var loaderUnavailable = new Error.Unavailable { Code = reason };
        var options = new ResourceAuthorizationOptions().HideExistence<HiddenResource>();
        var behavior = CreateBehaviorWithLoaderError(actorId: "owner-1", loaderUnavailable, options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        // Hiding transient failures behind 404 would destroy operational signal.
        result.UnwrapError().Should().BeSameAs(loaderUnavailable);
    }

    [Fact]
    public async Task Handle_HideExistenceConfigured_UnauthenticatedActorTranslatesAuthRequiredToNotFound()
    {
        // The actor-resolution branch (line 91 in ResourceAuthorizationBehavior.cs) emits
        // Error.AuthenticationRequired, which must also be hidden when HideAsNotFound is on.
        //
        // Note on the AuthorizationBehavior pipeline-composition caveat: the canonical Trellis
        // pipeline runs static-permission AuthorizationBehavior BEFORE ResourceAuthorization
        // Behavior. When a command implements both IAuthorize AND IAuthorizeResource<T>, an
        // unauthenticated caller fails the static gate first and that AuthenticationRequired
        // is NOT translated to NotFound — AuthorizationBehavior has no concept of the resource
        // being protected. This unit test covers only the inner behavior (the only scope where
        // translation occurs); the pipeline-composition limitation is documented (and not
        // asserted) in cookbook Recipe 32, ADR-002 §5.4, integration-mediator.md, and the
        // ResourceAuthorizationOptions reference. Consumers needing existence-hiding on
        // anonymous probes must omit IAuthorize.
        var options = new ResourceAuthorizationOptions().HideExistence<HiddenResource>();
        var behavior = CreateBehavior<HideExistenceCommand>(actorId: null, resource: new HiddenResource("res-1", "owner-1", "kind"), options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("HiddenResource");
    }

    [Fact]
    public async Task Handle_HideExistenceConfigured_PerResourcePropagateOverridesDefaultHideAsNotFound()
    {
        var resource = new HiddenResource("res-1", "owner-1", "k");
        var options = new ResourceAuthorizationOptions
        {
            DefaultExposurePolicy = AuthFailureExposurePolicy.HideAsNotFound,
        };
        options.Propagate<HiddenResource>();
        var behavior = CreateBehavior<HideExistenceCommand>(actorId: "other-user", resource, options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
    }

    [Fact]
    public async Task Handle_HideExistenceConfigured_DifferentResourceNotHidden()
    {
        var resource = new HiddenResource("res-1", "owner-1", "k");
        var options = new ResourceAuthorizationOptions().HideExistence<UnrelatedResource>();
        var behavior = CreateBehavior<HideExistenceCommand>(actorId: "other-user", resource, options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
    }

    [Fact]
    public async Task Handle_NoIdentifyResource_NotFoundEmittedWithoutId()
    {
        // ResourceOwnerCommand does not implement IIdentifyResource<TestResource, ?>, so the
        // reflection extractor returns null and the synthetic ResourceRef lacks an Id.
        var resource = new TestResource("res-1", "owner-1");
        var options = new ResourceAuthorizationOptions().HideExistence<TestResource>();
        var behavior = CreateOwnerBehavior(actorId: "other-user", resource, options);
        var command = new ResourceOwnerCommand("res-1");

        var result = await InvokeOwner(behavior, command);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("TestResource");
        notFound.Resource.Id.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ProjectionOverload_PublicResourceTypeIsExposedAndIdExtractedFromPublicIdentifier()
    {
        // HideExistence<TAuthorization, TPublic>() decouples loader projection from wire shape.
        // For projection, the command identifies the PUBLIC resource (PublicAggregate) and the
        // pipeline authorizes against the loaded projection (AuthorizationProjection).
        var projection = new AuthorizationProjection("owner-1");
        var options = new ResourceAuthorizationOptions()
            .HideExistence<AuthorizationProjection, PublicAggregate>();
        var behavior = CreateProjectionBehavior(actorId: "other-user", Result.Ok(projection), options);
        var command = new ProjectionAuthorizedCommand("public-id-42");

        var result = await InvokeProjection(behavior, command);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("PublicAggregate");
        notFound.Resource.Id.Should().Be("public-id-42");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_ProjectionOverload_LoadFailureAbsent_UsesPublicIdentifierAndCanonicalMetadata(bool gone)
    {
        var resourceRef = ResourceRef.For<AuthorizationProjection>("internal-id");
        Error original = gone ? new Error.Gone(resourceRef) : new Error.NotFound(resourceRef);
        original = original with
        {
            Code = gone ? "projection.gone" : "projection.missing",
            Detail = gone ? "The internal projection was purged." : "The internal projection was not found.",
            Cause = new Error.Forbidden("projection.hidden"),
        };
        var options = new ResourceAuthorizationOptions()
            .HideExistence<AuthorizationProjection, PublicAggregate>();
        var behavior = CreateProjectionBehavior("owner-1", Result.Fail<AuthorizationProjection>(original), options);

        var result = await InvokeProjection(behavior, new ProjectionAuthorizedCommand("public-id-42"));

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Should().Be(ResourceRef.For<PublicAggregate>("public-id-42"));
        notFound.Code.Should().Be("error.unspecified");
        notFound.Detail.Should().BeNull();
        notFound.Cause.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NullPayloadFromLoader_TranslatedToNotFoundUnderHideAsNotFound()
    {
        // The behavior's null-payload defense synthesises Error.Forbidden internally; under
        // HideAsNotFound that synthetic Forbidden must ALSO be hidden — a misbehaving loader
        // shouldn't leak existence either.
        var options = new ResourceAuthorizationOptions().HideExistence<HiddenResource>();
        var behavior = CreateBehaviorWithNullPayload(actorId: "owner-1", options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        result.UnwrapError().Should().BeOfType<Error.NotFound>();
    }

    [Fact]
    public async Task DI_HideExistenceConfigured_WithoutLoggingRegistered_StillAppliesPolicy()
    {
        // Regression for round-1 code-review finding: the new options-aware constructor's
        // `ILogger<...>` parameter must default to null so Microsoft DI's ActivatorUtilities
        // picks it even when the consumer hasn't called services.AddLogging(). Otherwise DI
        // would fall back to the legacy 2-arg ctor and silently drop the configured
        // HideExistence policy, leaving consumers vulnerable to existence leaks.
        var services = new ServiceCollection();

        services.AddResourceAuthorization<HideExistenceCommand, HiddenResource, Result<string>>();
        services.AddResourceAuthorization(o => o.HideExistence<HiddenResource>());

        services.AddScoped<IActorProvider>(_ => FakeActorProvider.NoPermissions("other-user"));
        services.AddScoped<IResourceLoader<HideExistenceCommand, HiddenResource>>(
            _ => new HiddenResourceLoader<HideExistenceCommand>(new HiddenResource("res-1", "owner-1", "k")));
        // NOTE: NO services.AddLogging() here — exercising the bare-bones consumer path.

        using var scope = services.BuildServiceProvider().CreateScope();
        var behavior = scope.ServiceProvider
            .GetRequiredService<IPipelineBehavior<HideExistenceCommand, Result<string>>>();
        behavior.Should().BeOfType<ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>>();

        var typedBehavior = (ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>)behavior;
        var result = await InvokeHide(typedBehavior, new HideExistenceCommand("res-1"));

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("HiddenResource");
        notFound.Resource.Id.Should().Be("res-1");
    }

    private static async Task<Result<string>> InvokeHide<TMessage>(
        ResourceAuthorizationBehavior<TMessage, HiddenResource, Result<string>> behavior,
        TMessage command)
        where TMessage : IAuthorizeResource<HiddenResource>, IMessage
    {
        var (next, _) = NextDelegate.TrackingAsync<TMessage, Result<string>>(Result.Ok("Done"));
        return await behavior.HandleWithContext(command, next, TestContext.Current.CancellationToken);
    }

    private static async Task<Result<string>> InvokeOwner(
        ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>> behavior,
        ResourceOwnerCommand command)
    {
        var (next, _) = NextDelegate.TrackingAsync<ResourceOwnerCommand, Result<string>>(Result.Ok("Done"));
        return await behavior.HandleWithContext(command, next, TestContext.Current.CancellationToken);
    }

    private static async Task<Result<string>> InvokeProjection(
        ResourceAuthorizationBehavior<ProjectionAuthorizedCommand, AuthorizationProjection, Result<string>> behavior,
        ProjectionAuthorizedCommand command)
    {
        var (next, _) = NextDelegate.TrackingAsync<ProjectionAuthorizedCommand, Result<string>>(Result.Ok("Done"));
        return await behavior.HandleWithContext(command, next, TestContext.Current.CancellationToken);
    }

    private static ResourceAuthorizationBehavior<TMessage, HiddenResource, Result<string>>
        CreateBehavior<TMessage>(
            string? actorId,
            HiddenResource resource,
            ResourceAuthorizationOptions options)
        where TMessage : IAuthorizeResource<HiddenResource>, IMessage
    {
        var loader = new HiddenResourceLoader<TMessage>(resource);
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<TMessage, HiddenResource>>(_ => loader);
        var provider = services.BuildServiceProvider();
        IActorProvider actorProvider = actorId is null
            ? FakeActorProvider.Anonymous()
            : FakeActorProvider.NoPermissions(actorId);
        return new ResourceAuthorizationBehavior<TMessage, HiddenResource, Result<string>>(
            actorProvider,
            provider,
            Options.Create(options),
            logger: NullLogger<ResourceAuthorizationBehavior<TMessage, HiddenResource, Result<string>>>.Instance);
    }

    private static ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>
        CreateBehaviorWithLoaderError(
            string actorId,
            Error loaderError,
            ResourceAuthorizationOptions options,
            ILogger<ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>>? logger = null)
    {
        var loader = new ErrorReturningLoader<HideExistenceCommand>(loaderError);
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<HideExistenceCommand, HiddenResource>>(_ => loader);
        var provider = services.BuildServiceProvider();
        return new ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>(
            FakeActorProvider.NoPermissions(actorId),
            provider,
            Options.Create(options),
            logger: logger ?? NullLogger<ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>>.Instance);
    }

    private static ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>
        CreateBehaviorWithNullPayload(
            string actorId,
            ResourceAuthorizationOptions options)
    {
        var loader = new NullPayloadHiddenLoader();
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<HideExistenceCommand, HiddenResource>>(_ => loader);
        var provider = services.BuildServiceProvider();
        return new ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>(
            FakeActorProvider.NoPermissions(actorId),
            provider,
            Options.Create(options),
            logger: NullLogger<ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>>.Instance);
    }

    private static ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>
        CreateOwnerBehavior(
            string actorId,
            TestResource resource,
            ResourceAuthorizationOptions options)
    {
        var loader = new TestResourceLoader(resource);
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<ResourceOwnerCommand, TestResource>>(_ => loader);
        var provider = services.BuildServiceProvider();
        return new ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>(
            FakeActorProvider.NoPermissions(actorId),
            provider,
            Options.Create(options),
            logger: NullLogger<ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>>.Instance);
    }

    private static ResourceAuthorizationBehavior<ProjectionAuthorizedCommand, AuthorizationProjection, Result<string>>
        CreateProjectionBehavior(
            string actorId,
            Result<AuthorizationProjection> loadResult,
            ResourceAuthorizationOptions options)
    {
        var loader = new ProjectionLoader(loadResult);
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<ProjectionAuthorizedCommand, AuthorizationProjection>>(_ => loader);
        var provider = services.BuildServiceProvider();
        return new ResourceAuthorizationBehavior<ProjectionAuthorizedCommand, AuthorizationProjection, Result<string>>(
            FakeActorProvider.NoPermissions(actorId),
            provider,
            Options.Create(options),
            logger: NullLogger<ResourceAuthorizationBehavior<ProjectionAuthorizedCommand, AuthorizationProjection, Result<string>>>.Instance);
    }

    internal sealed record HiddenResource(string Id, string OwnerId, string Kind);

    internal sealed record HideExistenceCommand(string ResourceId, Error? AuthorizationError = null)
        : ICommand<Result<string>>,
          IAuthorizeResource<HiddenResource>,
          IIdentifyResource<HiddenResource, string>
    {
        public string GetResourceId() => ResourceId;

        public IResult Authorize(Actor actor, HiddenResource resource) =>
            AuthorizationError is not null
                ? Result.Fail(AuthorizationError)
                : actor.Id == resource.OwnerId
                ? Result.Ok()
                : Result.Fail(new Error.Forbidden("authorization.forbidden"));
    }

    internal sealed record UnrelatedResource;

    internal sealed record AuthorizationProjection(string OwnerId);

    internal sealed record PublicAggregate;

    internal sealed record ProjectionAuthorizedCommand(string PublicId)
        : ICommand<Result<string>>,
          IAuthorizeResource<AuthorizationProjection>,
          IIdentifyResource<PublicAggregate, string>
    {
        public string GetResourceId() => PublicId;

        public IResult Authorize(Actor actor, AuthorizationProjection projection) =>
            actor.Id == projection.OwnerId
                ? Result.Ok()
                : Result.Fail(new Error.Forbidden("authorization.forbidden"));
    }

    private sealed class HiddenResourceLoader<TMessage>(HiddenResource resource)
        : IResourceLoader<TMessage, HiddenResource>
    {
        public Task<Result<HiddenResource>> LoadAsync(TMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(resource));
    }

    private sealed class ErrorReturningLoader<TMessage>(Error error)
        : IResourceLoader<TMessage, HiddenResource>
    {
        public Task<Result<HiddenResource>> LoadAsync(TMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Fail<HiddenResource>(error));
    }

    private sealed class NullPayloadHiddenLoader : IResourceLoader<HideExistenceCommand, HiddenResource>
    {
        public Task<Result<HiddenResource>> LoadAsync(HideExistenceCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok<HiddenResource>(null!));
    }

    private sealed class TestResourceLoader(TestResource resource)
        : IResourceLoader<ResourceOwnerCommand, TestResource>
    {
        public Task<Result<TestResource>> LoadAsync(ResourceOwnerCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(resource));
    }

    private sealed class ProjectionLoader(Result<AuthorizationProjection> result)
        : IResourceLoader<ProjectionAuthorizedCommand, AuthorizationProjection>
    {
        public Task<Result<AuthorizationProjection>> LoadAsync(ProjectionAuthorizedCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}