namespace Trellis.Asp.Authorization.Tests;

using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis.Authorization;

/// <summary>
/// Tests for <see cref="ServiceCollectionExtensions.AddTrellisWorkerActor(IServiceCollection, Actor)"/>
/// and the wrapper / validator that back it.
/// </summary>
public class WorkerActorTests
{
    private static readonly Actor SystemActor = Actor.Create(
        id: "system",
        permissions: new HashSet<string> { "reminders:dispatch", "reminders:read" });

    private static readonly Actor InnerActor = Actor.Create(
        id: "inner-user",
        permissions: new HashSet<string> { "orders:read" });

    [Fact]
    public void AddTrellisWorkerActor_throws_when_no_prior_IActorProvider_registered()
    {
        var services = new ServiceCollection();

        Action act = () => services.AddTrellisWorkerActor(SystemActor);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*requires a prior unkeyed IActorProvider registration*");
    }

    [Fact]
    public void AddTrellisWorkerActor_throws_when_called_twice()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActorProvider, FakeInnerProvider>();
        services.AddTrellisWorkerActor(SystemActor);

        Action act = () => services.AddTrellisWorkerActor(SystemActor);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already been called*");
    }

    [Fact]
    public void AddTrellisWorkerActor_throws_when_systemActor_is_null()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActorProvider, FakeInnerProvider>();

        Action act = () => services.AddTrellisWorkerActor(systemActor: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddTrellisWorkerActor_replaces_IActorProvider_slot_with_wrapper()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActorProvider, FakeInnerProvider>();

        services.AddTrellisWorkerActor(SystemActor);

        services.Where(d => d.ServiceType == typeof(IActorProvider))
            .Should().HaveCount(1, "AddTrellisWorkerActor must replace, not append");

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var resolved = scope.ServiceProvider.GetRequiredService<IActorProvider>();
        resolved.Should().BeOfType<WorkerComposedActorProvider>();
    }

    [Fact]
    public async Task Worker_scope_with_null_HttpContext_resolves_to_systemActor()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActorProvider, FakeInnerProvider>();
        services.AddTrellisWorkerActor(SystemActor);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = null;

        var provider = scope.ServiceProvider.GetRequiredService<IActorProvider>();
        var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.HasValue.Should().BeTrue();
        actor.Value.Id.Value.Should().Be("system");
        actor.Value.Permissions.Should().Contain("reminders:dispatch");
    }

    [Fact]
    public async Task HTTP_scope_with_HttpContext_delegates_to_inner_provider()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActorProvider, FakeInnerProvider>();
        services.AddTrellisWorkerActor(SystemActor);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();

        var provider = scope.ServiceProvider.GetRequiredService<IActorProvider>();
        var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.HasValue.Should().BeTrue();
        actor.Value.Id.Value.Should().Be("inner-user", "wrapper must delegate to inner when HttpContext is present");
    }

    [Fact]
    public async Task HTTP_scope_preserves_Maybe_None_from_inner_provider()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActorProvider, AnonymousFakeInnerProvider>();
        services.AddTrellisWorkerActor(SystemActor);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();

        var provider = scope.ServiceProvider.GetRequiredService<IActorProvider>();
        var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.HasValue.Should().BeFalse("anonymous HTTP request must surface as Maybe.None to drive 401, not silently grant SystemActor");
    }

    [Fact]
    public async Task Validator_throws_at_start_when_AddXxxActorProvider_called_AFTER_AddTrellisWorkerActor()
    {
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();
        services.AddTrellisWorkerActor(SystemActor);

        // Simulate the documented footgun: a later call overwrites the wrapper.
        services.AddEntraActorProvider();

        using var sp = services.BuildServiceProvider();
        var validator = sp.GetServices<IHostedService>()
            .OfType<WorkerActorRegistrationValidator>()
            .Single();

        Func<Task> act = () => validator.StartingAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*worker composition has been overwritten*");
    }

    [Fact]
    public async Task Validator_does_not_throw_when_wrapper_is_still_the_active_provider()
    {
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();
        services.AddTrellisWorkerActor(SystemActor);

        using var sp = services.BuildServiceProvider();
        var validator = sp.GetServices<IHostedService>()
            .OfType<WorkerActorRegistrationValidator>()
            .Single();

        await validator.StartingAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Wrapper_composes_with_CachingActorProvider_on_inside()
    {
        // Composition order: HTTP-side -> caching wrap -> worker wrap. Worker sits on the
        // outside so worker-tick lookups never invoke the caching layer (no scope to cache
        // against), while HTTP lookups flow through the caching decorator unchanged.
        var services = new ServiceCollection();
        services.AddCachingActorProvider<FakeInnerProvider>();
        services.AddTrellisWorkerActor(SystemActor);

        using var sp = services.BuildServiceProvider();
        using var workerScope = sp.CreateScope();
        workerScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = null;

        var workerActor = await workerScope.ServiceProvider
            .GetRequiredService<IActorProvider>().GetCurrentActorAsync(TestContext.Current.CancellationToken);
        workerActor.Value.Id.Value.Should().Be("system");

        using var httpScope = sp.CreateScope();
        httpScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();

        var httpActor = await httpScope.ServiceProvider
            .GetRequiredService<IActorProvider>().GetCurrentActorAsync(TestContext.Current.CancellationToken);
        httpActor.Value.Id.Value.Should().Be("inner-user", "HTTP path must traverse caching -> inner provider");
    }

    [Fact]
    public void Wrapper_VaryByHeaders_delegate_to_inner_when_inner_implements_IProvideActorVaryHeaders()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActorProvider, FakeInnerProviderWithVary>();
        services.AddTrellisWorkerActor(SystemActor);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var wrapper = (IProvideActorVaryHeaders)scope.ServiceProvider.GetRequiredService<IActorProvider>();

        wrapper.VaryByHeaders.Should().Contain("Authorization");
    }

    [Fact]
    public void Wrapper_VaryByHeaders_returns_empty_when_inner_does_not_implement_IProvideActorVaryHeaders()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActorProvider, FakeInnerProvider>();
        services.AddTrellisWorkerActor(SystemActor);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var wrapper = (IProvideActorVaryHeaders)scope.ServiceProvider.GetRequiredService<IActorProvider>();

        wrapper.VaryByHeaders.Should().BeEmpty();
    }

    [Fact]
    public async Task Worker_tick_with_null_HttpContext_does_not_materialize_inner_provider()
    {
        // Lazy materialization: a tick scope that only resolves the system actor must not
        // construct the HTTP-side provider (which may touch HttpContext in its ctor, or have
        // dependencies that are unavailable outside a request).
        var services = new ServiceCollection();
        var innerConstructions = 0;
        services.AddScoped<IActorProvider>(_ =>
        {
            innerConstructions++;
            return new FakeInnerProvider();
        });
        services.AddTrellisWorkerActor(SystemActor);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = null;

        var provider = scope.ServiceProvider.GetRequiredService<IActorProvider>();
        _ = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        innerConstructions.Should().Be(0, "worker tick must not construct the inner HTTP-side provider");
    }

    [Fact]
    public async Task HTTP_scope_materializes_inner_provider_exactly_once_per_wrapper_scope()
    {
        var services = new ServiceCollection();
        var innerConstructions = 0;
        services.AddScoped<IActorProvider>(_ =>
        {
            innerConstructions++;
            return new FakeInnerProvider();
        });
        services.AddTrellisWorkerActor(SystemActor);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();

        var provider = scope.ServiceProvider.GetRequiredService<IActorProvider>();
        _ = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);
        _ = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);
        _ = ((IProvideActorVaryHeaders)provider).VaryByHeaders;

        innerConstructions.Should().Be(1, "lazy inner must be materialized exactly once per wrapper scope");
    }

    [Fact]
    public async Task Wrapper_disposes_inner_when_wrapper_owns_it_factory_descriptor()
    {
        var services = new ServiceCollection();
        var disposable = new DisposableFakeInnerProvider();
        services.AddScoped<IActorProvider>(_ => disposable);
        services.AddTrellisWorkerActor(SystemActor);

        await using (var sp = services.BuildServiceProvider())
        {
            await using var scope = sp.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();
            _ = await scope.ServiceProvider.GetRequiredService<IActorProvider>()
                .GetCurrentActorAsync(TestContext.Current.CancellationToken);
        }

        disposable.DisposeCount.Should().Be(1, "wrapper owns the inner constructed from a factory descriptor and must dispose it");
    }

    [Fact]
    public async Task Wrapper_does_not_dispose_inner_when_descriptor_is_implementation_instance()
    {
        var services = new ServiceCollection();
        var disposable = new DisposableFakeInnerProvider();
        services.AddSingleton<IActorProvider>(disposable);
        services.AddTrellisWorkerActor(SystemActor);

        await using (var sp = services.BuildServiceProvider())
        {
            await using var scope = sp.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();
            _ = await scope.ServiceProvider.GetRequiredService<IActorProvider>()
                .GetCurrentActorAsync(TestContext.Current.CancellationToken);
        }

        disposable.DisposeCount.Should().Be(0,
            "consumer-supplied singleton instances belong to the consumer; the wrapper must not dispose them");
    }

    [Fact]
    public async Task Validator_names_the_LAST_registered_provider_as_active_type()
    {
        // DI's default GetRequiredService<T> returns the last-registered descriptor when
        // multiple are present. The diagnostic must point at the same provider DI would
        // actually resolve, not the first registration in the list.
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();
        services.AddTrellisWorkerActor(SystemActor);
        services.AddScoped<IActorProvider, FakeInnerProvider>();

        using var sp = services.BuildServiceProvider();
        var validator = sp.GetServices<IHostedService>()
            .OfType<WorkerActorRegistrationValidator>()
            .Single();

        Func<Task> act = () => validator.StartingAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*active provider type is 'FakeInnerProvider'*");
    }

    [Fact]
    public void AddTrellisWorkerActor_throws_on_singleton_lifetime_via_implementation_type()
    {
        // Singleton type registration would be silently downgraded to "one instance per
        // wrapper scope" — fail at registration time with a clear actionable diagnostic.
        var services = new ServiceCollection();
        services.AddSingleton<IActorProvider, FakeInnerProvider>();

        Action act = () => services.AddTrellisWorkerActor(SystemActor);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*singleton-lifetime IActorProvider*ImplementationType*ImplementationFactory*");
    }

    [Fact]
    public void AddTrellisWorkerActor_throws_on_singleton_lifetime_via_implementation_factory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActorProvider>(_ => new FakeInnerProvider());

        Action act = () => services.AddTrellisWorkerActor(SystemActor);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*singleton-lifetime IActorProvider*");
    }

    [Fact]
    public async Task AddTrellisWorkerActor_accepts_singleton_implementation_instance()
    {
        // ImplementationInstance is the safe singleton shape — wrapper delegates to it
        // without re-materializing and does not own its disposal.
        var services = new ServiceCollection();
        var singleton = new FakeInnerProvider();
        services.AddSingleton<IActorProvider>(singleton);
        services.AddTrellisWorkerActor(SystemActor);

        await using var sp = services.BuildServiceProvider();
        await using var httpScope = sp.CreateAsyncScope();
        httpScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();

        var actor = await httpScope.ServiceProvider.GetRequiredService<IActorProvider>()
            .GetCurrentActorAsync(TestContext.Current.CancellationToken);
        actor.Value.Id.Value.Should().Be("inner-user");
    }

    [Fact]
    public void AddTrellisWorkerActor_throws_on_transient_lifetime_via_implementation_type()
    {
        // Transient type registration would be silently upgraded to "one instance per
        // wrapper scope" instead of "fresh instance per resolution" — opposite of the
        // singleton case but the same kind of silent semantic change. Fail fast.
        var services = new ServiceCollection();
        services.AddTransient<IActorProvider, FakeInnerProvider>();

        Action act = () => services.AddTrellisWorkerActor(SystemActor);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*transient-lifetime IActorProvider*scoped semantics*");
    }

    [Fact]
    public void AddTrellisWorkerActor_throws_on_transient_lifetime_via_implementation_factory()
    {
        var services = new ServiceCollection();
        services.AddTransient<IActorProvider>(_ => new FakeInnerProvider());

        Action act = () => services.AddTrellisWorkerActor(SystemActor);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*transient-lifetime IActorProvider*");
    }

    [Fact]
    public void Validator_implements_IHostedLifecycleService_so_validation_runs_before_any_StartAsync()
    {
        // .NET's host invokes IHostedLifecycleService.StartingAsync on every hosted service
        // BEFORE any hosted service's IHostedService.StartAsync. The validator must implement
        // IHostedLifecycleService — not just IHostedService — so its check fires before any
        // BackgroundService registered alongside it has a chance to begin ExecuteAsync and
        // dispatch a mediator command with the wrong actor.
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();
        services.AddTrellisWorkerActor(SystemActor);

        using var sp = services.BuildServiceProvider();
        var validator = sp.GetServices<IHostedService>()
            .OfType<WorkerActorRegistrationValidator>()
            .Single();

        validator.Should().BeAssignableTo<IHostedLifecycleService>(
            "the host invokes StartingAsync on IHostedLifecycleService instances before any IHostedService.StartAsync");
    }

    [Fact]
    public async Task Validator_lifecycle_methods_other_than_StartingAsync_are_noops()
    {
        // StartAsync / StartedAsync / StoppingAsync / StopAsync / StoppedAsync exist only to
        // satisfy the IHostedLifecycleService contract — the actual validation runs in
        // StartingAsync. They must complete without throwing or doing work even when the
        // configuration is invalid, so the host shutdown path is not affected by validator
        // state.
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();
        services.AddTrellisWorkerActor(SystemActor);
        services.AddScoped<IActorProvider, FakeInnerProvider>(); // would fail StartingAsync

        using var sp = services.BuildServiceProvider();
        var validator = sp.GetServices<IHostedService>()
            .OfType<WorkerActorRegistrationValidator>()
            .Single();
        var ct = TestContext.Current.CancellationToken;

        await validator.StartAsync(ct);
        await validator.StartedAsync(ct);
        await validator.StoppingAsync(ct);
        await validator.StopAsync(ct);
        await validator.StoppedAsync(ct);
    }

    [Fact]
    public async Task Wrapper_disposes_inner_when_registered_via_implementation_type()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActorProvider, DisposableFakeInnerProvider>();
        services.AddTrellisWorkerActor(SystemActor);

        DisposableFakeInnerProvider? captured = null;

        await using (var sp = services.BuildServiceProvider())
        {
            await using var scope = sp.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();
            _ = await scope.ServiceProvider.GetRequiredService<IActorProvider>()
                .GetCurrentActorAsync(TestContext.Current.CancellationToken);

            captured = (DisposableFakeInnerProvider)((IDecoratingActorProvider)
                scope.ServiceProvider.GetRequiredService<IActorProvider>()).Inner;
        }

        captured!.DisposeCount.Should().Be(1, "wrapper owns ImplementationType-registered inner and must dispose it");
    }

    [Fact]
    public async Task Wrapper_sync_Dispose_skips_async_only_inner()
    {
        // Sync scope disposal (CreateScope, not CreateAsyncScope) must not bridge to
        // DisposeAsync for async-only inners because that can deadlock under a captured sync
        // context. Consumers with async-only resources must dispose the scope asynchronously.
        var services = new ServiceCollection();
        var asyncOnlyInner = new AsyncOnlyDisposableInnerProvider();
        services.AddScoped<IActorProvider>(_ => asyncOnlyInner);
        services.AddTrellisWorkerActor(SystemActor);

        using (var sp = services.BuildServiceProvider())
        using (var scope = sp.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();
            _ = await scope.ServiceProvider.GetRequiredService<IActorProvider>()
                .GetCurrentActorAsync(TestContext.Current.CancellationToken);
        }

        asyncOnlyInner.DisposeAsyncCount.Should().Be(0,
            "sync Dispose skips async-only IAsyncDisposable inners to avoid deadlock");
    }

    [Fact]
    public async Task Wrapper_materializes_inner_exactly_once_under_concurrent_access()
    {
        // Lazy<T> with ExecutionAndPublication ensures the inner factory runs exactly once
        // across concurrent VaryByHeaders / GetCurrentActorAsync calls — required so the
        // ownership/disposal accounting matches reality (only one instance to dispose).
        var services = new ServiceCollection();
        var innerConstructions = 0;
        services.AddScoped<IActorProvider>(_ =>
        {
            Interlocked.Increment(ref innerConstructions);
            return new FakeInnerProvider();
        });
        services.AddTrellisWorkerActor(SystemActor);

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();
        var provider = scope.ServiceProvider.GetRequiredService<IActorProvider>();
        var varyHeaders = (IProvideActorVaryHeaders)provider;

        var tasks = Enumerable.Range(0, 64).Select(i => Task.Run(() =>
        {
            if ((i & 1) == 0)
            {
                _ = provider.GetCurrentActorAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            }
            else
            {
                _ = varyHeaders.VaryByHeaders;
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        innerConstructions.Should().Be(1, "Lazy<T> ExecutionAndPublication must serialize concurrent materialization");
    }

    [Fact]
    public async Task Keyed_IActorProvider_registrations_are_ignored_and_preserved()
    {
        // Keyed registrations share the IActorProvider service type but are not the slot
        // GetRequiredService<IActorProvider>() resolves. They must be ignored by the count
        // check AND preserved after Replace (the helper must remove only the unkeyed slot).
        var services = new ServiceCollection();
        services.AddScoped<IActorProvider, FakeInnerProvider>();
        services.AddKeyedScoped<IActorProvider, FakeInnerProvider>("audit");
        services.AddKeyedScoped<IActorProvider, FakeInnerProvider>("tenant-x");

        services.AddTrellisWorkerActor(SystemActor);

        // Worker wrapper replaced unkeyed slot.
        services.Where(d => d.ServiceType == typeof(IActorProvider) && !d.IsKeyedService)
            .Should().HaveCount(1);
        // Both keyed registrations untouched.
        services.Where(d => d.ServiceType == typeof(IActorProvider) && d.IsKeyedService)
            .Should().HaveCount(2);

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = null;
        var workerActor = await scope.ServiceProvider.GetRequiredService<IActorProvider>()
            .GetCurrentActorAsync(TestContext.Current.CancellationToken);
        workerActor.Value.Id.Value.Should().Be("system", "unkeyed slot resolves to the worker wrapper");
    }

    [Fact]
    public void AddTrellisWorkerActor_throws_when_only_keyed_registrations_exist()
    {
        var services = new ServiceCollection();
        services.AddKeyedScoped<IActorProvider, FakeInnerProvider>("audit");

        Action act = () => services.AddTrellisWorkerActor(SystemActor);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*prior unkeyed IActorProvider registration*");
    }

    private sealed class FakeInnerProvider : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Maybe.From(InnerActor));
    }

    private sealed class DisposableFakeInnerProvider : IActorProvider, IDisposable
    {
        public int DisposeCount { get; private set; }

        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Maybe.From(InnerActor));

        public void Dispose() => DisposeCount++;
    }

    private sealed class AsyncOnlyDisposableInnerProvider : IActorProvider, IAsyncDisposable
    {
        public int DisposeAsyncCount { get; private set; }

        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Maybe.From(InnerActor));

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AnonymousFakeInnerProvider : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Maybe<Actor>.None);
    }

    private sealed class FakeInnerProviderWithVary : IActorProvider, IProvideActorVaryHeaders
    {
        public IReadOnlyCollection<string> VaryByHeaders { get; } = ["Authorization"];

        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Maybe.From(InnerActor));
    }
}
