using Trellis.Testing;
namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="SharedResourceLoaderById{TResource, TId}"/> integration with
/// <see cref="ServiceCollectionExtensions.AddResourceAuthorization(IServiceCollection, System.Reflection.Assembly[])"/>.
/// </summary>
public class SharedResourceLoaderTests
{
    #region Scanning discovers SharedResourceLoaderById and registers adapters

    [Fact]
    public void Scanning_RegistersAdapterForCommandWithIIdentifyResource()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(SharedCancelCommand).Assembly);

        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IResourceLoader<SharedCancelCommand, SharedOrder>));

        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    #endregion

    #region Multiple commands sharing the same resource type all get adapters

    [Fact]
    public void Scanning_RegistersAdapterForMultipleCommandsSharingResource()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(SharedCancelCommand).Assembly);

        var cancelLoader = services.SingleOrDefault(
            d => d.ServiceType == typeof(IResourceLoader<SharedCancelCommand, SharedOrder>));
        var returnLoader = services.SingleOrDefault(
            d => d.ServiceType == typeof(IResourceLoader<SharedReturnCommand, SharedOrder>));

        cancelLoader.Should().NotBeNull();
        returnLoader.Should().NotBeNull();
    }

    #endregion

    #region Explicit IResourceLoader takes priority over shared loader

    [Fact]
    public void Scanning_ExplicitLoaderTakesPriority()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(ExplicitLoaderCommand).Assembly);

        var descriptors = services
            .Where(d => d.ServiceType == typeof(IResourceLoader<ExplicitLoaderCommand, SharedOrder>))
            .ToList();

        // Explicit loader should be registered; no adapter should be registered
        descriptors.Should().ContainSingle();
        descriptors[0].ImplementationType.Should().Be<ExplicitOrderLoader>();
    }

    #endregion

    #region Pre-registered loader takes priority over shared adapter

    [Fact]
    public void Scanning_DoesNotOverridePreRegisteredLoader()
    {
        var services = new ServiceCollection();

        // Pre-register a factory loader BEFORE scanning
        services.AddScoped<IResourceLoader<SharedReturnCommand, SharedOrder>>(
            _ => new TestSharedOrderInlineLoader());

        services.AddResourceAuthorization(typeof(SharedCancelCommand).Assembly);

        // Scanning should NOT add another registration for the same service type
        // (neither the scanned concrete type nor the adapter)
        var descriptors = services
            .Where(d => d.ServiceType == typeof(IResourceLoader<SharedReturnCommand, SharedOrder>))
            .ToList();

        descriptors.Should().ContainSingle();
        descriptors[0].ImplementationFactory.Should().NotBeNull("the pre-registered factory should be the only registration");
    }

    [Fact]
    public void AddSharedResourceLoader_DoesNotOverrideExistingLoader()
    {
        var services = new ServiceCollection();
        services.AddScoped<SharedResourceLoaderById<SharedOrder, string>, TestSharedOrderLoader>();

        // Pre-register an explicit loader
        services.AddScoped<IResourceLoader<SharedReturnCommand, SharedOrder>>(
            _ => new TestSharedOrderInlineLoader());

        // AddSharedResourceLoader should not override it
        services.AddSharedResourceLoader<SharedReturnCommand, SharedOrder, string>();

        var descriptors = services
            .Where(d => d.ServiceType == typeof(IResourceLoader<SharedReturnCommand, SharedOrder>))
            .ToList();

        descriptors.Should().ContainSingle();
        descriptors[0].ImplementationFactory.Should().NotBeNull("the pre-registered factory should be the only registration");
    }

    #endregion

    #region Command without IIdentifyResource is NOT auto-bridged

    [Fact]
    public void Scanning_DoesNotBridgeCommandWithoutIIdentifyResource()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(NonIdentifyCommand).Assembly);

        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IResourceLoader<NonIdentifyCommand, SharedOrder>));

        descriptor.Should().BeNull();
    }

    #endregion

    #region SharedResourceLoaderById is registered as scoped

    [Fact]
    public void Scanning_RegistersSharedLoaderAsScoped()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(SharedCancelCommand).Assembly);

        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(SharedResourceLoaderById<SharedOrder, string>));

        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    #endregion

    #region Mismatched TId is NOT auto-bridged

    [Fact]
    public void Scanning_DoesNotBridgeWhenTIdDoesNotMatchSharedLoader()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(MismatchedIdCommand).Assembly);

        // SharedResourceLoaderById<SharedOrder, string> exists but command uses int as TId
        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IResourceLoader<MismatchedIdCommand, SharedOrder>));

        descriptor.Should().BeNull();
    }

    #endregion

    #region Adapter resolves and delegates correctly

    [Fact]
    public async Task Adapter_ScannedRegistration_ForwardsOriginalTaskIdTokenAndResource()
    {
        using var cts = new CancellationTokenSource();
        var order = new SharedOrder("order-1", "owner-1");
        var response = Task.FromResult(Result.Ok(order));
        var inner = new RecordingSharedResourceLoader<SharedOrder, string>(response);
        var services = new ServiceCollection();
        services.AddScoped<SharedResourceLoaderById<SharedOrder, string>>(_ => inner);
        services.AddResourceAuthorization(typeof(SharedCancelCommand).Assembly);
        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var loader = scope.ServiceProvider
            .GetRequiredService<IResourceLoader<SharedCancelCommand, SharedOrder>>();

        var forwarded = loader.LoadAsync(new SharedCancelCommand("order-1"), cts.Token);

        forwarded.Should().BeSameAs(response);
        inner.Calls.Should().Be(1);
        inner.LastRequestedId.Should().Be("order-1");
        inner.LastCancellationToken.Should().Be(cts.Token);
        var result = await forwarded;
        result.IsSuccess.Should().BeTrue();
        var loaded = result.Unwrap();
        loaded.Id.Should().Be("order-1");
        loaded.Should().BeSameAs(order);
    }

    #endregion

    #region Explicit AOT-safe registration

    [Fact]
    public void AddSharedResourceLoader_RegistersAdapter()
    {
        var services = new ServiceCollection();
        services.AddScoped<SharedResourceLoaderById<SharedOrder, string>, TestSharedOrderLoader>();

        services.AddSharedResourceLoader<SharedCancelCommand, SharedOrder, string>();

        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IResourceLoader<SharedCancelCommand, SharedOrder>));

        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Theory]
    [InlineData(TaskStatus.RanToCompletion)]
    [InlineData(TaskStatus.WaitingForActivation)]
    [InlineData(TaskStatus.Faulted)]
    [InlineData(TaskStatus.Canceled)]
    public async Task AddSharedResourceLoader_ResolvedAdapter_ForwardsOriginalTaskIdAndToken(TaskStatus taskStatus)
    {
        using var cts = new CancellationTokenSource();
        var order = new SharedOrder("order-42", "owner-1");
        var failure = new InvalidOperationException("loader failed");
        var completion = new TaskCompletionSource<Result<SharedOrder>>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (taskStatus == TaskStatus.Canceled)
            cts.Cancel();
        var response = taskStatus switch
        {
            TaskStatus.RanToCompletion => Task.FromResult(Result.Ok(order)),
            TaskStatus.WaitingForActivation => completion.Task,
            TaskStatus.Faulted => Task.FromException<Result<SharedOrder>>(failure),
            TaskStatus.Canceled => Task.FromCanceled<Result<SharedOrder>>(cts.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(taskStatus))
        };
        var inner = new RecordingSharedResourceLoader<SharedOrder, string>(response);
        var services = new ServiceCollection();
        services.AddScoped<SharedResourceLoaderById<SharedOrder, string>>(_ => inner);
        services.AddSharedResourceLoader<SharedCancelCommand, SharedOrder, string>();
        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var loader = scope.ServiceProvider
            .GetRequiredService<IResourceLoader<SharedCancelCommand, SharedOrder>>();

        var forwarded = loader.LoadAsync(new SharedCancelCommand("order-42"), cts.Token);
        try
        {
            forwarded.Should().BeSameAs(response);
            inner.Calls.Should().Be(1);
            inner.LastRequestedId.Should().Be("order-42");
            inner.LastCancellationToken.Should().Be(cts.Token);
            if (taskStatus == TaskStatus.WaitingForActivation)
                forwarded.IsCompleted.Should().BeFalse("the original pending task must be returned before it completes");
        }
        finally
        {
            completion.TrySetResult(Result.Ok(order));
        }

        var act = async () => await forwarded;
        if (taskStatus == TaskStatus.Faulted)
        {
            var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
            thrown.Which.Should().BeSameAs(failure);
        }
        else if (taskStatus == TaskStatus.Canceled)
        {
            var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
            thrown.Which.CancellationToken.Should().Be(cts.Token);
        }
        else
        {
            var result = await forwarded;
            result.IsSuccess.Should().BeTrue();
            var loaded = result.Unwrap();
            loaded.Id.Should().Be("order-42");
            loaded.Should().BeSameAs(order);
        }
    }

    #endregion

    #region SharedResourceLoaderAdapter argument-null guards (PR #459 review)

    /// <summary>
    /// PR #459 review feedback: the constructor and <c>LoadAsync</c> null-guards added to
    /// <c>SharedResourceLoaderAdapter</c> (m-1) had no regression coverage. The adapter is
    /// internal but reachable from this Tests assembly via the repo-wide
    /// <c>InternalsVisibleTo</c> convention.
    /// </summary>
    [Fact]
    public void SharedResourceLoaderAdapter_NullInner_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => new SharedResourceLoaderAdapter<SharedCancelCommand, SharedOrder, string>(inner: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "inner");

    [Fact]
    public async Task SharedResourceLoaderAdapter_LoadAsync_NullMessage_ThrowsArgumentNullException()
    {
        var adapter = new SharedResourceLoaderAdapter<SharedCancelCommand, SharedOrder, string>(
            new TestSharedOrderLoader());

        await FluentActions
            .Invoking(() => adapter.LoadAsync(message: null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "message");
    }

    #endregion

    #region Test helpers

    private sealed class RecordingSharedResourceLoader<TResource, TId>(Task<Result<TResource>> response)
        : SharedResourceLoaderById<TResource, TId>
    {
        public int Calls { get; private set; }
        public TId? LastRequestedId { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public override Task<Result<TResource>> GetByIdAsync(TId id, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequestedId = id;
            LastCancellationToken = cancellationToken;
            return response;
        }
    }

    public sealed record SharedOrder(string Id, string OwnerId);

    // -- Shared loader (one per resource type)

    public sealed class TestSharedOrderLoader : SharedResourceLoaderById<SharedOrder, string>
    {
        public override Task<Result<SharedOrder>> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new SharedOrder(id, "owner-1")));
    }

    // -- Commands that use IIdentifyResource (should be auto-bridged)

    public sealed record SharedCancelCommand(string OrderId)
        : ICommand<Result<Unit>>, IAuthorizeResource<SharedOrder>, IIdentifyResource<SharedOrder, string>
    {
        public string GetResourceId() => OrderId;
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Ok();
    }

    public sealed record SharedReturnCommand(string OrderId)
        : ICommand<Result<Unit>>, IAuthorizeResource<SharedOrder>, IIdentifyResource<SharedOrder, string>
    {
        public string GetResourceId() => OrderId;
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Ok();
    }

    // -- Command WITHOUT IIdentifyResource (should NOT be auto-bridged)

    public sealed record NonIdentifyCommand(string OrderId)
        : ICommand<Result<Unit>>, IAuthorizeResource<SharedOrder>
    {
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Ok();
    }

    // -- Command with explicit loader (explicit should win)

    public sealed record ExplicitLoaderCommand(string OrderId)
        : ICommand<Result<Unit>>, IAuthorizeResource<SharedOrder>, IIdentifyResource<SharedOrder, string>
    {
        public string GetResourceId() => OrderId;
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Ok();
    }

    public sealed class ExplicitOrderLoader : IResourceLoader<ExplicitLoaderCommand, SharedOrder>
    {
        public Task<Result<SharedOrder>> LoadAsync(ExplicitLoaderCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new SharedOrder(message.OrderId, "explicit-owner")));
    }

    // -- Loader for pre-registration tests (scanning will also discover this via Assembly.GetTypes(),
    //    but TryAddScoped ensures the pre-registered factory wins)

    private sealed class TestSharedOrderInlineLoader : IResourceLoader<SharedReturnCommand, SharedOrder>
    {
        public Task<Result<SharedOrder>> LoadAsync(SharedReturnCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new SharedOrder(message.OrderId, "inline")));
    }

    // -- Command with mismatched TId (shared loader uses string, command uses int)

    public sealed record MismatchedIdCommand(int OrderNumber)
        : ICommand<Result<Unit>>, IAuthorizeResource<SharedOrder>, IIdentifyResource<SharedOrder, int>
    {
        public int GetResourceId() => OrderNumber;
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Ok();
    }

    #endregion
}