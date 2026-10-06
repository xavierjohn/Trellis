#if GENERATED_PIPELINE
namespace ActorHandlers.GeneratedPipeline;
#else
namespace ActorHandlers.Consumer;
#endif

using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

internal static class ConsumerScenarios
{
    internal static async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await AllFamiliesAsync(cancellationToken).ConfigureAwait(false);
        await MultipleResourcesAsync(cancellationToken).ConfigureAwait(false);
        await DenialAndMissingGatesAsync(cancellationToken).ConfigureAwait(false);
        await NestedDispatchAsync(cancellationToken).ConfigureAwait(false);
        await ConcurrentDispatchAsync(cancellationToken).ConfigureAwait(false);
        await ExpiredSnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task MultipleResourcesAsync(CancellationToken token)
    {
        foreach (var (folderFirst, denyFolder) in new[] { (false, false), (false, true), (true, false), (true, true) })
        {
            var actor = CreateActor("owner");
            var provider = new SnapshotProvider();
            provider.Prepare(actor, CreateActor("changed"));
            var registrations = CreateServices(provider);
            if (folderFirst)
                registrations.AddResourceAuthorization<MultiResourceCommand, Folder, Result<Observation>>();
            registrations.AddResourceAuthorization<MultiResourceCommand, Order, Result<Observation>>();
            if (!folderFirst)
                registrations.AddResourceAuthorization<MultiResourceCommand, Folder, Result<Observation>>();
            registrations.AddScoped<IResourceLoader<MultiResourceCommand, Order>, OrderLoader<MultiResourceCommand>>();
            registrations.AddScoped<IResourceLoader<MultiResourceCommand, Folder>, CommandFolderLoader>();

            await using var services = registrations.BuildServiceProvider(validateScopes: true);
            using var scope = services.CreateScope();
            var probe = new DispatchProbe(actor);
            var folderProbe = new DispatchProbe(denyFolder ? CreateActor("intruder") : actor);
            var result = await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new MultiResourceCommand("order", probe, folderProbe), token).ConfigureAwait(false);
            VerifyPipeline<MultiResourceCommand>(scope.ServiceProvider, 2);
            if (denyFolder)
            {
                Check(result.Error is Error.Forbidden && probe.BusinessCalls == 0,
                    "A denied resource check must block the handler even when its selected resource was already authorized.");
                Check(provider.Calls == 1 && folderProbe.Loads == 1 && probe.Loads == (folderFirst ? 0 : 1),
                    "A denied resource check duplicated resolution or failed to short-circuit loading.");
                continue;
            }

            var observed = RequireSuccess(result);
            Check(ReferenceEquals(observed.Actor, actor) && ReferenceEquals(observed.Resource, probe.Loaded),
                "Multiple resource checks must preserve the handler's checked actor and selected resource.");
            Check(ReferenceEquals(probe.CheckedActor, actor) && ReferenceEquals(folderProbe.CheckedActor, actor),
                "Multiple resource checks used different actor snapshots.");
            Check(provider.Calls == 1 && probe.Loads == 1 && folderProbe.Loads == 1 && probe.BusinessCalls == 1,
                "Multiple resource registration duplicated resolution, loading, or business execution.");
            Check(observed.Token == token, "The multiple-resource dispatch token was not forwarded.");
        }
    }

    private static async Task AllFamiliesAsync(CancellationToken token)
    {
        var provider = new SnapshotProvider();
        await using var services = CreateServices(provider).BuildServiceProvider(validateScopes: true);
        using var scope = services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var first = CreateActor("owner");
        var changed = new Actor("owner", new HashSet<string>(), new HashSet<string>(), new Dictionary<string, string> { ["snapshot"] = "changed" });
        var probes = Enumerable.Range(0, 7).Select(_ => new DispatchProbe(first)).ToArray();
        Func<ValueTask<Result<Observation>>>[] sends =
        [
            () => sender.Send(new AuditCommand(probes[0]), token),
            () => sender.Send(new ActorQuery(probes[1]), token),
            () => sender.Send(new UpdateOrderCommand("order-command", probes[2]), token),
            () => sender.Send(new OrderQuery("order-query", probes[3]), token),
            () => sender.Send(new UpdateDocumentCommand("document-command", probes[4]), token),
            () => sender.Send(new DocumentQuery("document-query", probes[5]), token),
            () => sender.Send(new AccessorQuery("accessor", probes[6]), token),
        ];
        for (var index = 0; index < sends.Length; index++)
        {
            provider.Prepare(first, changed);
            var observed = RequireSuccess(await sends[index]().ConfigureAwait(false));
            Check(ReferenceEquals(observed.Actor, first), "Business logic must receive the checked Actor reference, not the same-ID replacement.");
            Check(observed.Token == token, "The dispatch token was not forwarded.");
            Check(provider.Calls == 1, "Authorization must resolve the actor exactly once per dispatch.");
            Check(probes[index].BusinessCalls == 1, "A handler ran more than once.");
            Check(ReferenceEquals(observed.Resource, probes[index].Loaded), "A handler did not receive the loaded resource/leaf reference.");
            Check(probes[index].Loads == (index >= 2 ? 1 : 0), "The resource was not loaded exactly once.");
            if (index >= 2)
                Check(ReferenceEquals(probes[index].CheckedActor, first), "Resource authorization checked a different actor snapshot.");
        }

        VerifyPipeline<AuditCommand>(scope.ServiceProvider, 0);
        VerifyPipeline<ActorQuery>(scope.ServiceProvider, 0);
        VerifyPipeline<UpdateOrderCommand>(scope.ServiceProvider, 1);
        VerifyPipeline<OrderQuery>(scope.ServiceProvider, 1);
        VerifyPipeline<UpdateDocumentCommand>(scope.ServiceProvider, 1);
        VerifyPipeline<DocumentQuery>(scope.ServiceProvider, 1);
        Check(scope.ServiceProvider.GetRequiredService<AuditWriter>().Entries == 6,
            "The migrated handlers did not retain their business-only dependency.");

        var handler = new UpdateOrderHandler(new AuditWriter());
        var explicitActor = CreateActor("explicit");
        var explicitResource = new Order("explicit", "explicit");
        var explicitProbe = new DispatchProbe(explicitActor);
        var explicitResult = RequireSuccess(await handler.Handle(new("explicit", explicitProbe), explicitActor, explicitResource, token).ConfigureAwait(false));
        Check(ReferenceEquals(explicitResult.Actor, explicitActor) && ReferenceEquals(explicitResult.Resource, explicitResource),
            "The business-test overload must use supplied arguments without ambient state.");
        Check(explicitProbe.Loads == 0 && provider.Calls == 1, "The explicit overload performed pipeline work.");
    }

    private static async Task DenialAndMissingGatesAsync(CancellationToken token)
    {
        var provider = new SnapshotProvider();
        await using var services = CreateServices(provider).BuildServiceProvider(validateScopes: true);
        using var scope = services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var expected = CreateActor("owner");

        foreach (var via in new[] { false, true })
        {
            foreach (var anonymous in new[] { false, true })
            {
                var probe = new DispatchProbe(expected);
                provider.Prepare(anonymous ? null : CreateActor("intruder"));
                var result = via
                    ? await sender.Send(new DocumentQuery("private", probe), token).ConfigureAwait(false)
                    : await sender.Send(new OrderQuery("private", probe), token).ConfigureAwait(false);
                Check(result.Error is Error.NotFound, "Resource-only authorization must retain existence hiding.");
                Check(probe.BusinessCalls == 0 && probe.Loads == (anonymous ? 0 : 1),
                    "A denied or anonymous dispatch executed business logic or anonymous resource I/O.");
                Check(provider.Calls == 1, "A denied dispatch resolved the actor more than once.");
            }
        }

        var staticDenied = new DispatchProbe(expected);
        provider.Prepare(Actor.Create("owner", new HashSet<string>()));
        var denied = await sender.Send(new UpdateOrderCommand("private", staticDenied), token).ConfigureAwait(false);
        Check(denied.Error is Error.Forbidden && staticDenied.Loads == 0 && staticDenied.BusinessCalls == 0,
            "Static denial must occur before resource loading and must not be hidden by a resource policy.");

        var direct = new AuditCommandHandler(new AuditWriter());
        await ExpectFaultAsync(() => direct.Handle(new AuditCommand(new(expected)), token), "AuthorizationContextBehavior").ConfigureAwait(false);

        var missingProvider = new SnapshotProvider();
        missingProvider.Prepare(expected);
        await using var missingServices = CreateServices(missingProvider, registerResources: false).BuildServiceProvider(validateScopes: true);
        using var missingScope = missingServices.CreateScope();
        var missingProbe = new DispatchProbe(expected);
        var missing = await missingScope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new UpdateOrderCommand("unregistered", missingProbe), token).ConfigureAwait(false);
        Check(missing.Error is Error.Unexpected && missingProbe.BusinessCalls == 0,
            "A handler with a missing resource gate must fail before business logic.");
    }

    private static async Task NestedDispatchAsync(CancellationToken token)
    {
        for (var outcome = 0; outcome < 5; outcome++)
        {
            var provider = new SnapshotProvider();
            var outerActor = CreateActor("outer");
            var innerActor = outcome == 1 ? Actor.Create("inner", new HashSet<string>()) : CreateActor("inner");
            provider.Prepare(outerActor, innerActor, CreateActor("query"));
            await using var services = CreateServices(provider).BuildServiceProvider(validateScopes: true);
            using var scope = services.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var writer = scope.ServiceProvider.GetRequiredService<AuditWriter>();
            var outerProbe = new DispatchProbe(outerActor);
            var innerProbe = new DispatchProbe(innerActor);
            using var cancellation = new CancellationTokenSource();
            var currentOutcome = outcome;
            var outer = new AuditCommand(outerProbe, async (actor, dispatchToken) =>
            {
                Check(ReferenceEquals(actor, outerActor), "The outer actor snapshot changed.");
                var inner = new AuditCommand(innerProbe, (checkedActor, innerToken) =>
                {
                    Check(ReferenceEquals(checkedActor, innerActor), "The inner dispatch borrowed the outer actor.");
                    if (currentOutcome == 2)
                        return new(Result.Fail<Observation>(new Error.Conflict(Resource: null, Code: "business.failed")));
                    if (currentOutcome == 3)
                        throw new InvalidOperationException("business exception");
                    if (currentOutcome == 4)
                        innerToken.ThrowIfCancellationRequested();
                    return new(writer.Record(innerProbe, checkedActor, null, innerToken));
                });
                if (currentOutcome == 4)
                    cancellation.Cancel();
                try
                {
                    var innerResult = await sender.Send(inner, currentOutcome == 4 ? cancellation.Token : dispatchToken).ConfigureAwait(false);
                    Check(currentOutcome == 0 ? innerResult.IsSuccess : innerResult.IsFailure, "The nested outcome changed.");
                }
                catch (OperationCanceledException) when (currentOutcome == 4)
                {
                    Check(cancellation.IsCancellationRequested, "Unexpected cancellation.");
                }

                if (currentOutcome == 0)
                    Check(RequireSuccess(await sender.Send(new ActorQuery(new(CreateActor("query"))), dispatchToken).ConfigureAwait(false)).Actor.Id.Value == "query",
                        "Different-type nested dispatch borrowed the outer snapshot.");

                var restored = await new AuditCommandHandler(writer).Handle(new AuditCommand(outerProbe), dispatchToken).ConfigureAwait(false);
                Check(ReferenceEquals(RequireSuccess(restored).Actor, outerActor), "Unwinding did not restore the outer snapshot.");
                return restored;
            });

            RequireSuccess(await sender.Send(outer, token).ConfigureAwait(false));
            Check(provider.Calls == (outcome == 0 ? 3 : 2), "Nested dispatches did not each resolve their own actor.");
            Check(outcome != 1 || innerProbe.BusinessCalls == 0, "Denied inner dispatch executed business logic.");
            await ExpectFaultAsync(() => new AuditCommandHandler(writer).Handle(new AuditCommand(outerProbe), token),
                "No authorization dispatch").ConfigureAwait(false);
        }
    }

    private static async Task ConcurrentDispatchAsync(CancellationToken token)
    {
        var firstActor = CreateActor("first");
        var secondActor = CreateActor("second");
        var provider = new SnapshotProvider();
        provider.Prepare(firstActor, secondActor);
        await using var services = CreateServices(provider).BuildServiceProvider(validateScopes: true);
        using var scope = services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var bothLoading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        Task WaitForBoth(CancellationToken ct)
        {
            if (Interlocked.Increment(ref arrivals) == 2)
                bothLoading.SetResult();
            return bothLoading.Task.WaitAsync(ct);
        }

        var first = new DispatchProbe(firstActor) { BeforeLoad = WaitForBoth };
        var second = new DispatchProbe(secondActor) { BeforeLoad = WaitForBoth };
        var firstTask = sender.Send(new UpdateOrderCommand("first", first), token).AsTask();
        var secondTask = sender.Send(new UpdateOrderCommand("second", second), token).AsTask();
        var results = await Task.WhenAll(firstTask, secondTask).ConfigureAwait(false);
        Check(ReferenceEquals(RequireSuccess(results[0]).Actor, firstActor) && ReferenceEquals(RequireSuccess(results[0]).Resource, first.Loaded),
            "The first concurrent dispatch was contaminated.");
        Check(ReferenceEquals(RequireSuccess(results[1]).Actor, secondActor) && ReferenceEquals(RequireSuccess(results[1]).Resource, second.Loaded),
            "The second concurrent dispatch was contaminated.");
        Check(provider.Calls == 2 && first.Loads == 1 && second.Loads == 1, "Concurrent dispatch execution was duplicated.");
    }

    private static async Task ExpiredSnapshotAsync(CancellationToken token)
    {
        var actor = CreateActor("owner");
        var provider = new SnapshotProvider();
        provider.Prepare(actor, actor);
        await using var services = CreateServices(provider).BuildServiceProvider(validateScopes: true);
        using var scope = services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var writer = scope.ServiceProvider.GetRequiredService<AuditWriter>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? orphan = null;
        var probe = new DispatchProbe(actor);
        var inner = new AuditCommand(probe, (checkedActor, ct) =>
        {
            orphan = Task.Run(async () =>
            {
                await release.Task.ConfigureAwait(false);
                await ExpectFaultAsync(() => new AuditCommandHandler(writer).Handle(new AuditCommand(probe), ct),
                    "expired").ConfigureAwait(false);
            }, token);
            return new(writer.Record(probe, checkedActor, null, ct));
        });
        var outer = new AuditCommand(new(actor), async (checkedActor, ct) =>
        {
            RequireSuccess(await sender.Send(inner, ct).ConfigureAwait(false));
            release.SetResult();
            await (orphan ?? throw new InvalidOperationException("The orphan task was not created.")).ConfigureAwait(false);
            return writer.Record(new(actor), checkedActor, null, ct);
        });
        RequireSuccess(await sender.Send(outer, token).ConfigureAwait(false));
    }

    private static ServiceCollection CreateServices(SnapshotProvider provider, bool registerResources = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IActorProvider>(provider);
        services.AddScoped<AuditWriter>();
#if GENERATED_PIPELINE || NATIVE_AOT
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.PipelineBehaviors =
            [
                typeof(ExceptionBehavior<,>), typeof(TracingBehavior<,>), typeof(LoggingBehavior<,>),
                typeof(AuthorizationContextBehavior<,>), typeof(AuthorizationBehavior<,>), typeof(ValidationBehavior<,>),
            ];
        });
#else
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddTrellisBehaviors();
        services.AddTrellisBehaviors();
#endif
        services.AddResourceAuthorization(options => options.HideExistence<Order>().HideExistence<Document>());
        if (!registerResources)
            return services;
        for (var index = 0; index < 2; index++)
        {
            services.AddResourceAuthorization<UpdateOrderCommand, Order, Result<Observation>>();
            services.AddResourceAuthorization<OrderQuery, Order, Result<Observation>>();
            services.AddResourceAuthorization<AccessorQuery, Order, Result<Observation>>();
            services.AddRelatedResourceAuthorization<UpdateDocumentCommand, Document, string, Folder, string, Result<Observation>>(document => document.FolderId);
            services.AddRelatedResourceAuthorization<DocumentQuery, Document, string, Folder, string, Result<Observation>>(document => document.FolderId);
        }

        services.AddScoped<IResourceLoader<UpdateOrderCommand, Order>, OrderLoader<UpdateOrderCommand>>();
        services.AddScoped<IResourceLoader<OrderQuery, Order>, OrderLoader<OrderQuery>>();
        services.AddScoped<IResourceLoader<AccessorQuery, Order>, OrderLoader<AccessorQuery>>();
        services.AddScoped<IResourceLoader<UpdateDocumentCommand, Document>, DocumentLoader<UpdateDocumentCommand>>();
        services.AddScoped<IResourceLoader<DocumentQuery, Document>, DocumentLoader<DocumentQuery>>();
        services.AddScoped<SharedResourceLoaderById<Folder, string>, FolderLoader>();
        return services;
    }

    private static void VerifyPipeline<TMessage>(IServiceProvider services, int resourceBehaviors)
        where TMessage : IMessage, IAuthorizationMessage
    {
        var behaviors = services.GetServices<IPipelineBehavior<TMessage, Result<Observation>>>().ToArray();
        Check(behaviors.Count(behavior => behavior is AuthorizationContextBehavior<TMessage, Result<Observation>>) == 1,
            $"Generated/runtime registration installed duplicate authorization contexts for {typeof(TMessage)}.");
        Check(behaviors.Count(behavior => behavior.GetType().Name.StartsWith("ResourceAuthorization", StringComparison.Ordinal)) == resourceBehaviors,
            "Resource authorization registration was duplicated.");
    }

    private static Actor CreateActor(string id) => new(id, new HashSet<string> { "read", "write" }, new HashSet<string>(),
        new Dictionary<string, string> { ["snapshot"] = "checked" });

    private static Observation RequireSuccess(Result<Observation> result)
    {
        if (!result.TryGetValue(out var value))
            throw new InvalidOperationException($"Generated dispatch failed: {result.Error}.");
        return value;
    }

    private static void Check(bool condition, string detail)
    {
        if (!condition)
            throw new InvalidOperationException(detail);
    }

    private static async Task ExpectFaultAsync(Func<ValueTask<Result<Observation>>> invoke, string expectedMessage)
    {
        try
        {
            await invoke().ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            Check(exception.Message.Contains(expectedMessage, StringComparison.Ordinal), $"Unexpected diagnostic: {exception.Message}");
            return;
        }

        throw new InvalidOperationException("A normal handler entry unexpectedly accepted missing/expired authorization state.");
    }

    private sealed class SnapshotProvider : IActorProvider
    {
        private readonly object _lock = new();
        private Queue<Actor?> _actors = new();
        internal int Calls { get; private set; }

        internal void Prepare(params Actor?[] actors)
        {
            lock (_lock)
            {
                _actors = new(actors);
                Calls = 0;
            }
        }

        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                Calls++;
                if (!_actors.TryDequeue(out var actor))
                    throw new InvalidOperationException("Actor resolution exceeded the prepared dispatch snapshots.");
                return Task.FromResult(actor is null ? Maybe<Actor>.None : Maybe.From(actor));
            }
        }
    }
}
