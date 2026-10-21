namespace Trellis.ServiceDefaults.Tests;

using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;
using Unit = Trellis.Unit;

public class CommandPreCommitHookBuilderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UseCommandPreCommitHook_BeforeOrAfterUnitOfWork_PreservesCanonicalOrder(bool unitOfWorkFirst)
    {
        var services = new ServiceCollection();
        services.AddTrellis(options =>
        {
            if (unitOfWorkFirst)
                options.UseEntityFrameworkUnitOfWork<TestDbContext>();

            options.UseCommandPreCommitHook<Command, Result<string>, Hook>().UseDomainEvents();

            if (!unitOfWorkFirst)
                options.UseEntityFrameworkUnitOfWork<TestDbContext>();
        });

        AssertCanonicalOrder(services);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AddCommandPreCommitHook_MixedRegistrationOrder_PreservesCanonicalOrder(
        bool unitOfWorkFirst, bool mediatorFirst)
    {
        var services = new ServiceCollection();
        if (mediatorFirst)
            services.AddTrellisBehaviors();
        if (unitOfWorkFirst)
            services.AddTrellisUnitOfWork<TestDbContext>();

        services.AddCommandPreCommitHook<Command, Result<string>, Hook>();
        services.AddDomainEventDispatch();

        if (!unitOfWorkFirst)
            services.AddTrellisUnitOfWork<TestDbContext>();
        if (!mediatorFirst)
            services.AddTrellisBehaviors();

        AssertCanonicalOrder(services);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseCommandPreCommitHook_DirectAndBuilderOverlap_ExecutesOnce(bool directFirst)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        if (directFirst)
            services.AddCommandPreCommitHook<Command, Result<string>, Hook>();

        services.AddTrellis(options => options
            .UseCommandPreCommitHook<Command, Result<string>, Hook>()
            .UseCommandPreCommitHook<Command, Result<string>, Hook>());

        if (!directFirst)
            services.AddCommandPreCommitHook<Command, Result<string>, Hook>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        using var scope = provider.CreateScope();
        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<Command, Result<string>>>().ToArray();
        var transaction = behaviors.OfType<TransactionalCommandBehavior<Command, Result<string>>>().Single();
        var hook = scope.ServiceProvider.GetRequiredService<ICommandPreCommitHook<Command, Result<string>>>();

        var result = await transaction.Handle(new Command(),
            (_, _) => Result.Ok("done").AsValueTask(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        ((Hook)hook).Calls.Should().Be(1);
        behaviors.Should().Contain(b => b is ValidationBehavior<Command, Result<string>>);
        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(ICommandPreCommitHook<Command, Result<string>>));
    }

    private static void AssertCanonicalOrder(IServiceCollection services) =>
        services.Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .Should().Equal(
            [
                typeof(ExceptionBehavior<,>),
                typeof(TracingBehavior<,>),
                typeof(LoggingBehavior<,>),
                typeof(AuthorizationContextBehavior<,>),
                typeof(AuthorizationBehavior<,>),
                typeof(ValidationBehavior<,>),
                typeof(DomainEventDispatchBehavior<,>),
                typeof(TransactionalCommandBehavior<,>),
            ]);

    private sealed record Command : ICommand<Result<string>>;

    private sealed class Hook : ICommandPreCommitHook<Command, Result<string>>
    {
        public int Calls { get; private set; }

        public ValueTask<Result<Unit>> BeforeCommitAsync(
            Command message, Result<string> response, CancellationToken cancellationToken)
        {
            Calls++;
            return Result.Ok().AsValueTask();
        }
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);

    private sealed class UnitOfWork : IUnitOfWork
    {
        public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default) => Result.Ok().AsTask();
        public IUnitOfWorkScope BeginScope() => new Scope();

        private sealed class Scope : IUnitOfWorkScope
        {
            public bool IsOwner => true;
            public void Dispose() { }
        }
    }
}
