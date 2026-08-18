namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;
using Trellis.Testing;

/// <summary>
/// Tests for the explicit (non-scanning) registration helper
/// <see cref="ServiceCollectionExtensions.AddRelatedResourceAuthorization{TMessage, TLeaf, TLeafId, TOwner, TOwnerId, TResponse}"/>
/// — the AOT path that does not depend on assembly scanning.
/// </summary>
public class AddRelatedResourceAuthorizationTests
{
    [Fact]
    public async Task SingleHopOverload_RegistersBehaviorAndAuthorizesEndToEnd()
    {
        var services = new ServiceCollection();
        services.AddTrellisBehaviors();
        services.AddScoped<IActorProvider>(_ => FakeActorProvider.NoPermissions("actor-1"));

        // Explicit loader registrations — no assembly scan.
        var leafRepo = new ExplicitLeafRepo();
        leafRepo.Add(new ExplicitLeaf("l-1", "owner-1"));
        services.AddSingleton(leafRepo);
        services.AddScoped<SharedResourceLoaderById<ExplicitLeaf, string>, ExplicitLeafLoader>();
        services.AddSharedResourceLoader<ExplicitCommand, ExplicitLeaf, string>();

        var ownerRepo = new ExplicitOwnerRepo();
        ownerRepo.Add(new ExplicitOwner("owner-1", "actor-1"));
        services.AddSingleton(ownerRepo);
        services.AddScoped<SharedResourceLoaderById<ExplicitOwner, string>, ExplicitOwnerLoader>();

        services.AddRelatedResourceAuthorization<
            ExplicitCommand, ExplicitLeaf, string, ExplicitOwner, string, Result<string>>(
            leaf => leaf.OwnerId);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var behavior = scope.ServiceProvider
            .GetRequiredService<IPipelineBehavior<ExplicitCommand, Result<string>>>();
        var (next, tracker) = NextDelegate.TrackingAsync<ExplicitCommand, Result<string>>(
            Result.Ok("Done"));

        var result = await behavior.Handle(new ExplicitCommand("l-1"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tracker.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task SingleHopOverload_NullOwnerId_ShortCircuitsToForbidden()
    {
        var services = new ServiceCollection();
        services.AddTrellisBehaviors();
        services.AddScoped<IActorProvider>(_ => FakeActorProvider.NoPermissions("actor-1"));

        var leafRepo = new ExplicitLeafRepo();
        leafRepo.Add(new ExplicitLeaf("l-1", ownerId: null!));  // null id intentional
        services.AddSingleton(leafRepo);
        services.AddScoped<SharedResourceLoaderById<ExplicitLeaf, string>, ExplicitLeafLoader>();
        services.AddSharedResourceLoader<ExplicitCommand, ExplicitLeaf, string>();

        // Owner loader registered but won't be hit because extractor returns null.
        services.AddScoped<SharedResourceLoaderById<ExplicitOwner, string>>(_ =>
            new ExplicitOwnerLoader(new ExplicitOwnerRepo()));

        services.AddRelatedResourceAuthorization<
            ExplicitCommand, ExplicitLeaf, string, ExplicitOwner, string, Result<string>>(
            leaf => leaf.OwnerId);  // returns null, triggers Forbidden short-circuit

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var behavior = scope.ServiceProvider
            .GetRequiredService<IPipelineBehavior<ExplicitCommand, Result<string>>>();
        var (next, tracker) = NextDelegate.TrackingAsync<ExplicitCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(new ExplicitCommand("l-1"), next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
        result.UnwrapError().Code.Should().Be("resource.authorization-via.empty");
        tracker.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task SingleHopOverload_OwnerLoaderReturnsSuccessWithNullPayload_CollapsesToForbidden()
    {
        // Defense-in-depth: a SharedResourceLoaderById that violates its Result<T> contract
        // by returning Result.Ok carrying a null value must NOT crash the AOT registration
        // path with ArgumentNullException from HopLoadResult.Success — mirrors the same
        // defense in ResourceAuthorizationPathResolver so both the assembly-scan path and
        // the AOT helper preserve the documented "intermediate / owner load failure
        // collapses to Forbidden" invariant.
        var services = new ServiceCollection();
        services.AddTrellisBehaviors();
        services.AddScoped<IActorProvider>(_ => FakeActorProvider.NoPermissions("actor-1"));

        var leafRepo = new ExplicitLeafRepo();
        leafRepo.Add(new ExplicitLeaf("l-1", "owner-1"));
        services.AddSingleton(leafRepo);
        services.AddScoped<SharedResourceLoaderById<ExplicitLeaf, string>, ExplicitLeafLoader>();
        services.AddSharedResourceLoader<ExplicitCommand, ExplicitLeaf, string>();

        // Owner loader returns Result.Ok(null) — contract violation that must fail closed.
        services.AddScoped<SharedResourceLoaderById<ExplicitOwner, string>, NullPayloadOwnerLoader>();

        services.AddRelatedResourceAuthorization<
            ExplicitCommand, ExplicitLeaf, string, ExplicitOwner, string, Result<string>>(
            leaf => leaf.OwnerId);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var behavior = scope.ServiceProvider
            .GetRequiredService<IPipelineBehavior<ExplicitCommand, Result<string>>>();
        var (next, tracker) = NextDelegate.TrackingAsync<ExplicitCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(new ExplicitCommand("l-1"), next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        var error = result.UnwrapError();
        error.Should().BeOfType<Error.Forbidden>();
        // The via behavior masks the hop's inner sentinel ("null-payload") with the generic
        // "load-failed" code so the response does not leak which hop failed; what matters
        // here is that the pipeline FAILS CLOSED (Forbidden) instead of throwing
        // ArgumentNullException → 500 from HopLoadResult.Success(null!).
        error.Code.Should().Be("resource.authorization-via.load-failed");
        tracker.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public void SingleHopOverload_LandsBeforeValidationBehavior()
    {
        var services = new ServiceCollection();
        services.AddTrellisBehaviors();

        services.AddRelatedResourceAuthorization<
            ExplicitCommand, ExplicitLeaf, string, ExplicitOwner, string, Result<string>>(
            leaf => leaf.OwnerId);

        // Find the indices of the registered closed-pipeline behavior and the open-generic
        // ValidationBehavior to assert relative ordering. The via-behavior is registered with
        // a TYPED ImplementationType (ResourceAuthorizationViaBehavior<,,,>).
        var pipelineType = typeof(IPipelineBehavior<ExplicitCommand, Result<string>>);
        var viaIndex = -1;
        for (int i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == pipelineType
                && services[i].ImplementationType is { IsGenericType: true } impl
                && impl.GetGenericTypeDefinition() == typeof(ResourceAuthorizationViaBehavior<,,,>))
            {
                viaIndex = i;
                break;
            }
        }

        var validationIndex = -1;
        for (int i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(IPipelineBehavior<,>)
                && services[i].ImplementationType == typeof(ValidationBehavior<,>))
            {
                validationIndex = i;
                break;
            }
        }

        viaIndex.Should().BeGreaterThanOrEqualTo(0);
        validationIndex.Should().BeGreaterThanOrEqualTo(0);
        viaIndex.Should().BeLessThan(validationIndex,
            "the via behavior must run before the validation behavior in the pipeline");
    }

    [Fact]
    public void SingleHopOverload_RegistrationBeforeAddTrellisBehaviors_StillRelocatesBeforeValidation()
    {
        // Order-independence: explicit registration that happens BEFORE AddTrellisBehaviors
        // should still land before ValidationBehavior after AddTrellisBehaviors runs.
        var services = new ServiceCollection();

        services.AddRelatedResourceAuthorization<
            ExplicitCommand, ExplicitLeaf, string, ExplicitOwner, string, Result<string>>(
            leaf => leaf.OwnerId);

        services.AddTrellisBehaviors();

        var pipelineType = typeof(IPipelineBehavior<ExplicitCommand, Result<string>>);
        var viaIndex = -1;
        for (int i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == pipelineType
                && services[i].ImplementationType is { IsGenericType: true } impl
                && impl.GetGenericTypeDefinition() == typeof(ResourceAuthorizationViaBehavior<,,,>))
            {
                viaIndex = i;
                break;
            }
        }

        var validationIndex = -1;
        for (int i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(IPipelineBehavior<,>)
                && services[i].ImplementationType == typeof(ValidationBehavior<,>))
            {
                validationIndex = i;
                break;
            }
        }

        viaIndex.Should().BeGreaterThanOrEqualTo(0);
        validationIndex.Should().BeGreaterThanOrEqualTo(0);
        viaIndex.Should().BeLessThan(validationIndex);
    }

    [Fact]
    public void Relocator_DoesNotMoveUnrelatedFactoryRegisteredPipelineBehaviors()
    {
        // A consumer may register their own closed-generic IPipelineBehavior<,> via a factory
        // for unrelated reasons (e.g., a behavior that needs captured options). The relocator
        // must NOT treat such descriptors as Trellis-owned and re-order them. The relocator
        // identifies Trellis-owned descriptors by ImplementationType
        // (ResourceAuthorizationBehavior<,,> or ResourceAuthorizationViaBehavior<,,,>) and
        // inherently ignores factory descriptors, so a consumer's factory-registered
        // IPipelineBehavior<,> with no ImplementationType is left alone.
        var services = new ServiceCollection();

        // Register an UNRELATED closed-generic factory-based pipeline behavior BEFORE
        // AddTrellisBehaviors so it appears earlier in the descriptor list than ValidationBehavior.
        var unrelatedBehavior = new UnrelatedPipelineBehavior();
        services.AddScoped<IPipelineBehavior<ExplicitCommand, Result<string>>>(_ => unrelatedBehavior);

        // Snapshot the unrelated behavior's index before AddTrellisBehaviors runs.
        var unrelatedIndexBefore = -1;
        for (int i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(IPipelineBehavior<ExplicitCommand, Result<string>>)
                && services[i].ImplementationFactory is not null)
            {
                unrelatedIndexBefore = i;
                break;
            }
        }

        services.AddTrellisBehaviors();

        // After AddTrellisBehaviors, the unrelated factory descriptor must still be present.
        // The relocator should NOT have moved it before ValidationBehavior just because it
        // looks like a closed-generic IPipelineBehavior<,>.
        var validationIndex = -1;
        var unrelatedIndexAfter = -1;
        for (int i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(IPipelineBehavior<ExplicitCommand, Result<string>>)
                && services[i].ImplementationFactory is not null)
                unrelatedIndexAfter = i;
            if (services[i].ServiceType == typeof(IPipelineBehavior<,>)
                && services[i].ImplementationType == typeof(ValidationBehavior<,>))
                validationIndex = i;
        }

        unrelatedIndexAfter.Should().BeGreaterThanOrEqualTo(0, "the unrelated behavior must still be registered");
        validationIndex.Should().BeGreaterThanOrEqualTo(0);

        // The relocator must NOT have touched the unrelated descriptor. It stays put at
        // exactly where the consumer placed it (index 0 in this test, since it was added
        // first before AddTrellisBehaviors ran). If the relocator had incorrectly classified
        // it as Trellis-owned (e.g., by matching factory descriptors), it would have been
        // pulled out and re-inserted just before ValidationBehavior (i.e. at validationIndex),
        // not at its original position 0.
        unrelatedIndexAfter.Should().Be(unrelatedIndexBefore,
            "the relocator only moves descriptors whose ImplementationType is " +
            "ResourceAuthorizationBehavior<,,> or ResourceAuthorizationViaBehavior<,,,>; " +
            "consumer factory-registered descriptors must be left where they were.");
    }

    [Fact]
    public async Task SingleHopOverload_OwnerLoaderMissing_Throws()
    {
        // Missing loader is a deployment bug, NOT an authorization denial.
        // The behavior must throw InvalidOperationException, not return Forbidden,
        // so persistent 403s caused by misconfiguration are not silently masked.
        var services = new ServiceCollection();
        services.AddTrellisBehaviors();
        services.AddScoped<IActorProvider>(_ => FakeActorProvider.NoPermissions("actor-1"));

        var leafRepo = new ExplicitLeafRepo();
        leafRepo.Add(new ExplicitLeaf("l-1", "owner-1"));
        services.AddSingleton(leafRepo);
        services.AddScoped<SharedResourceLoaderById<ExplicitLeaf, string>, ExplicitLeafLoader>();
        services.AddSharedResourceLoader<ExplicitCommand, ExplicitLeaf, string>();

        // INTENTIONALLY: no SharedResourceLoaderById<ExplicitOwner, string> registered.

        services.AddRelatedResourceAuthorization<
            ExplicitCommand, ExplicitLeaf, string, ExplicitOwner, string, Result<string>>(
            leaf => leaf.OwnerId);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var behavior = scope.ServiceProvider
            .GetRequiredService<IPipelineBehavior<ExplicitCommand, Result<string>>>();
        var (next, _) = NextDelegate.TrackingAsync<ExplicitCommand, Result<string>>(
            Result.Ok("should not reach"));

        Func<Task> act = async () =>
            await behavior.Handle(new ExplicitCommand("l-1"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SharedResourceLoaderById<ExplicitOwner, String>*");
    }

    [Fact]
    public void PathOverload_MismatchedMessageType_ThrowsAtRegistration()
    {
        // A hand-built ResolvedAuthorizationPath whose MessageType does not match the
        // AddRelatedResourceAuthorization<TMessage,...> generic argument must fail FAST at
        // registration time (not later when the behavior is constructed during the first
        // request). The validation lives in ResolvedAuthorizationPathHolder's constructor,
        // and the holder is constructed eagerly by AddRelatedResourceAuthorization.
        var services = new ServiceCollection();
        services.AddTrellisBehaviors();

        var pathWithWrongMessage = new ResolvedAuthorizationPath(
            messageType: typeof(string),  // wrong — should be ExplicitCommand
            leafType: typeof(ExplicitLeaf),
            ownerType: typeof(ExplicitOwner),
            hops:
            [
                new ResolvedAuthorizationHop(
                    fromType: typeof(ExplicitLeaf),
                    toType: typeof(ExplicitOwner),
                    toIdType: typeof(string),
                    extractIds: _ => Array.Empty<object>(),
                    loadAsync: (_, _, _) => Task.FromResult(HopLoadResult.Failure(new Error.Forbidden("x"))),
                    isPlural: false),
            ]);

        var act = () => services.AddRelatedResourceAuthorization<
            ExplicitCommand, ExplicitLeaf, ExplicitOwner, Result<string>>(pathWithWrongMessage);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*MessageType*String*ExplicitCommand*");
    }

    [Fact]
    public void PathOverload_MismatchedLeafType_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();
        services.AddTrellisBehaviors();

        var pathWithWrongLeaf = new ResolvedAuthorizationPath(
            messageType: typeof(ExplicitCommand),
            leafType: typeof(string),  // wrong — should be ExplicitLeaf
            ownerType: typeof(ExplicitOwner),
            hops:
            [
                new ResolvedAuthorizationHop(
                    fromType: typeof(string),
                    toType: typeof(ExplicitOwner),
                    toIdType: typeof(string),
                    extractIds: _ => Array.Empty<object>(),
                    loadAsync: (_, _, _) => Task.FromResult(HopLoadResult.Failure(new Error.Forbidden("x"))),
                    isPlural: false),
            ]);

        var act = () => services.AddRelatedResourceAuthorization<
            ExplicitCommand, ExplicitLeaf, ExplicitOwner, Result<string>>(pathWithWrongLeaf);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*LeafType*String*ExplicitLeaf*");
    }

    [Fact]
    public void PathOverload_MismatchedOwnerType_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();
        services.AddTrellisBehaviors();

        var pathWithWrongOwner = new ResolvedAuthorizationPath(
            messageType: typeof(ExplicitCommand),
            leafType: typeof(ExplicitLeaf),
            ownerType: typeof(string),  // wrong — should be ExplicitOwner
            hops:
            [
                new ResolvedAuthorizationHop(
                    fromType: typeof(ExplicitLeaf),
                    toType: typeof(string),
                    toIdType: typeof(string),
                    extractIds: _ => Array.Empty<object>(),
                    loadAsync: (_, _, _) => Task.FromResult(HopLoadResult.Failure(new Error.Forbidden("x"))),
                    isPlural: false),
            ]);

        var act = () => services.AddRelatedResourceAuthorization<
            ExplicitCommand, ExplicitLeaf, ExplicitOwner, Result<string>>(pathWithWrongOwner);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*OwnerType*String*ExplicitOwner*");
    }
}

internal sealed class UnrelatedPipelineBehavior : IPipelineBehavior<ExplicitCommand, Result<string>>
{
    public ValueTask<Result<string>> Handle(ExplicitCommand message, MessageHandlerDelegate<ExplicitCommand, Result<string>> next, CancellationToken cancellationToken)
        => next(message, cancellationToken);
}

#region Test fixtures

internal sealed record ExplicitCommand(string LeafId)
    : ICommand<Result<string>>,
      IAuthorizeResourceVia<ExplicitOwner>,
      IIdentifyResource<ExplicitLeaf, string>
{
    public string GetResourceId() => LeafId;

    public IResult Authorize(Actor actor, IReadOnlyList<ExplicitOwner> owners) =>
        owners.Any(o => o.CreatedByActorId == actor.Id)
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden("not_owner"));
}

internal sealed class ExplicitLeaf(string id, string ownerId)
    : IIdentifyRelatedResource<ExplicitOwner, string>
{
    public string Id { get; } = id;
    public string OwnerId { get; } = ownerId;

    // Declared so when other tests' assembly scans encounter ExplicitCommand
    // (which implements IAuthorizeResourceVia<ExplicitOwner>), the path resolver can find
    // a valid leaf→owner path and registration succeeds. The actual tests in this file use
    // the explicit AddRelatedResourceAuthorization overload, not the scanner.
    public string GetRelatedResourceId() => OwnerId;
}

internal sealed class ExplicitOwner(string id, string createdByActorId)
{
    public string Id { get; } = id;
    public string CreatedByActorId { get; } = createdByActorId;
}

internal sealed class ExplicitLeafRepo
{
    private readonly Dictionary<string, ExplicitLeaf> _items = [];
    public void Add(ExplicitLeaf l) => _items[l.Id] = l;
    public Result<ExplicitLeaf> GetById(string id) => _items.TryGetValue(id, out var v)
        ? Result.Ok(v)
        : Result.Fail<ExplicitLeaf>(new Error.NotFound(new ResourceRef("ExplicitLeaf", id)));
}

internal sealed class ExplicitOwnerRepo
{
    private readonly Dictionary<string, ExplicitOwner> _items = [];
    public void Add(ExplicitOwner o) => _items[o.Id] = o;
    public Result<ExplicitOwner> GetById(string id) => _items.TryGetValue(id, out var v)
        ? Result.Ok(v)
        : Result.Fail<ExplicitOwner>(new Error.NotFound(new ResourceRef("ExplicitOwner", id)));
}

internal sealed class ExplicitLeafLoader(ExplicitLeafRepo repo) : SharedResourceLoaderById<ExplicitLeaf, string>
{
    public override Task<Result<ExplicitLeaf>> GetByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(repo.GetById(id));
}

internal sealed class ExplicitOwnerLoader(ExplicitOwnerRepo repo) : SharedResourceLoaderById<ExplicitOwner, string>
{
    public override Task<Result<ExplicitOwner>> GetByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(repo.GetById(id));
}

internal sealed class NullPayloadOwnerLoader : SharedResourceLoaderById<ExplicitOwner, string>
{
    // Violates the Result<T> contract by returning Result.Ok carrying a null payload.
    // Used to assert the AOT helper's defense-in-depth fail-closed mapping to Forbidden.
    public override Task<Result<ExplicitOwner>> GetByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Ok<ExplicitOwner>(null!));
}

#endregion