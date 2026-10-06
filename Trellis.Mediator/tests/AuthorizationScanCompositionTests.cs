namespace Trellis.Mediator.Tests;

using ActorHandlers.ScanApplication;
using ActorHandlers.ScanPersistence;
using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trellis.EntityFrameworkCore;
using Trellis.Testing;

public class AuthorizationScanCompositionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AddResourceAuthorization_TwoAssembliesAndOptionsOrder_ExecutesOnceBeforeCommit(bool optionsFirst, bool via)
    {
        var callbacks = new List<string>();
        var services = CreateServices(optionsFirst, callbacks);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var probe = scope.ServiceProvider.GetRequiredService<ScanProbe>();
        var actor = Actor.Create("owner", new HashSet<string> { "write" });
        probe.Actor = actor;
        probe.ReplacementActor = Actor.Create("owner", new HashSet<string>());
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = via
            ? await sender.Send(new ScanViaCommand("resource"), TestContext.Current.CancellationToken)
            : await sender.Send(new ScanCommand("resource"), TestContext.Current.CancellationToken);

        result.Unwrap().Actor.Should().BeSameAs(actor);
        result.Unwrap().Resource.Should().BeSameAs(probe.Loaded);
        probe.ProviderCalls.Should().Be(1);
        probe.LeafLoads.Should().Be(1);
        probe.BusinessCalls.Should().Be(1);
        probe.Commits.Should().Be(1);
        probe.Operations.Should().Equal(via
            ? ["actor", "load", "load-owner", "business", "commit"]
            : ["actor", "load", "authorize", "business", "commit"]);
        callbacks.Should().Equal(["default", "propagate", "hide"]);

        var behaviors = via
            ? scope.ServiceProvider.GetServices<IPipelineBehavior<ScanViaCommand, Result<ScanObservation>>>()
                .Select(behavior => behavior.GetType().GetGenericTypeDefinition())
            : scope.ServiceProvider.GetServices<IPipelineBehavior<ScanCommand, Result<ScanObservation>>>()
                .Select(behavior => behavior.GetType().GetGenericTypeDefinition());
        behaviors.Should().Equal(
            typeof(ExceptionBehavior<,>), typeof(TracingBehavior<,>), typeof(LoggingBehavior<,>),
            typeof(AuthorizationContextBehavior<,>), typeof(AuthorizationBehavior<,>),
            via ? typeof(ResourceAuthorizationViaBehavior<,,,>) : typeof(ResourceAuthorizationBehavior<,,>),
            typeof(ValidationBehavior<,>), typeof(TransactionalCommandBehavior<,>));
        typeof(ScanCommand).Assembly.Should().NotBeSameAs(typeof(ScanResourceLoader).Assembly);
        services.Should().ContainSingle(descriptor => descriptor.ImplementationType == typeof(ScanResourceLoader));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AddResourceAuthorization_TwoAssembliesAndRepeatedCallbacks_PreservesEffectiveExposure(bool optionsFirst, bool anonymous)
    {
        var callbacks = new List<string>();
        var services = CreateServices(optionsFirst, callbacks);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        foreach (var via in new[] { false, true })
        {
            using var scope = provider.CreateScope();
            var probe = scope.ServiceProvider.GetRequiredService<ScanProbe>();
            probe.Actor = anonymous ? null : Actor.Create("intruder", new HashSet<string> { "write" });
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            var result = via
                ? await sender.Send(new ScanViaQuery("private"), TestContext.Current.CancellationToken)
                : await sender.Send(new ScanQuery("private"), TestContext.Current.CancellationToken);

            if (via)
                result.Error.Should().BeOfType(anonymous ? typeof(Error.AuthenticationRequired) : typeof(Error.Forbidden));
            else
                result.Error.Should().BeOfType<Error.NotFound>();
            probe.ProviderCalls.Should().Be(1);
            probe.LeafLoads.Should().Be(anonymous ? 0 : 1);
            probe.LoaderConstructions.Should().Be(anonymous ? 0 : 1);
            probe.BusinessCalls.Should().Be(0);
            probe.Commits.Should().Be(0);
            scope.ServiceProvider.GetRequiredService<IOptions<ResourceAuthorizationOptions>>()
                .Value.DefaultExposurePolicy.Should().Be(AuthFailureExposurePolicy.HideAsNotFound);
        }

        callbacks.Should().Equal(["default", "propagate", "hide"]);
    }

    private static ServiceCollection CreateServices(bool optionsFirst, List<string> callbacks)
    {
        var services = new ServiceCollection();
        services.AddScoped<ScanProbe>();
        services.AddScoped<IActorProvider, ScanActorProvider>();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScanMediator();
        services.AddTrellisBehaviors();
        if (optionsFirst)
            ConfigureOptions(services, callbacks);
        services.AddResourceAuthorization(typeof(ScanCommand).Assembly, typeof(ScanResourceLoader).Assembly);
        services.AddResourceAuthorization(typeof(ScanCommand).Assembly, typeof(ScanResourceLoader).Assembly);
        if (!optionsFirst)
            ConfigureOptions(services, callbacks);
        services.AddDbContext<ScanDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddTrellisUnitOfWork<ScanDbContext>();
        return services;
    }

    private static void ConfigureOptions(IServiceCollection services, List<string> callbacks)
    {
        services.AddResourceAuthorization(options =>
        {
            callbacks.Add("default");
            options.DefaultExposurePolicy = AuthFailureExposurePolicy.HideAsNotFound;
        });
        services.AddResourceAuthorization(options =>
        {
            callbacks.Add("propagate");
            options.Propagate<ScanDocument>();
        });
        services.AddResourceAuthorization(options =>
        {
            callbacks.Add("hide");
            options.HideExistence<ScanResource>();
        });
    }

    private sealed class ScanDbContext(DbContextOptions<ScanDbContext> options, ScanProbe probe) : DbContext(options)
    {
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            probe.Commits++;
            probe.Operations.Add("commit");
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }
}
