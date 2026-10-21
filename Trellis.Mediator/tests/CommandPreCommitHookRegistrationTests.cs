namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Testing;
using Unit = Trellis.Unit;

public class CommandPreCommitHookRegistrationTests
{
    [Fact]
    public void AddCommandPreCommitHook_NullServices_Throws() =>
        FluentActions.Invoking(() => CommandPreCommitHookServiceCollectionExtensions
                .AddCommandPreCommitHook<Command, Result<string>, FirstHook>(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");

    [Fact]
    public async Task AddCommandPreCommitHook_DuplicateBindings_ExecutesEachHookOnceBeforeCommit()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddCommandPreCommitHook<Command, Result<string>, FirstHook>();
        services.AddCommandPreCommitHook<Command, Result<string>, SecondHook>();
        services.AddCommandPreCommitHook<Command, Result<string>, FirstHook>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        using var scope = provider.CreateScope();
        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<Command, Result<string>>>().ToArray();
        behaviors.Should().ContainSingle().Which.Should()
            .BeOfType<TransactionalCommandBehavior<Command, Result<string>>>();

        var result = await behaviors[0].Handle(new Command(), (_, _) =>
        {
            calls.Add("handler");
            return Result.Ok("done").AsValueTask();
        }, TestContext.Current.CancellationToken);

        result.Should().HaveValue("done");
        calls.Should().Equal(["handler", "first", "second", "commit"]);
        services.Where(d => d.ServiceType == typeof(ICommandPreCommitHook<Command, Result<string>>))
            .Should().OnlyContain(d => d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddCommandPreCommitHook_ScopedBinding_ReusesOnlyWithinScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<List<string>>();
        services.AddCommandPreCommitHook<Command, Result<string>, FirstHook>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var hook = first.ServiceProvider.GetRequiredService<ICommandPreCommitHook<Command, Result<string>>>();

        first.ServiceProvider.GetRequiredService<ICommandPreCommitHook<Command, Result<string>>>()
            .Should().BeSameAs(hook);
        second.ServiceProvider.GetRequiredService<ICommandPreCommitHook<Command, Result<string>>>()
            .Should().NotBeSameAs(hook);
    }

    [Fact]
    public async Task AddCommandPreCommitHook_UnregisteredCommand_PreservesCommitWithoutHooks()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddCommandPreCommitHook<Command, Result<string>, FirstHook>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<ICommandPreCommitHook<OtherCommand, Result<string>>>()
            .Should().BeEmpty();
        var behavior = scope.ServiceProvider.GetRequiredService<IPipelineBehavior<OtherCommand, Result<string>>>();

        var result = await behavior.Handle(new OtherCommand(),
            (_, _) => Result.Ok("done").AsValueTask(), TestContext.Current.CancellationToken);

        result.Should().HaveValue("done");
        calls.Should().Equal(["commit"]);
    }

    [Fact]
    public void AddCommandPreCommitHook_MissingUnitOfWork_FailsInsteadOfIgnoringHook()
    {
        var services = new ServiceCollection();
        services.AddSingleton<List<string>>();
        services.AddCommandPreCommitHook<Command, Result<string>, FirstHook>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetServices<IPipelineBehavior<Command, Result<string>>>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ICommandPreCommitHook_QueryType_RejectsInvalidGenericArgument() =>
        FluentActions.Invoking(() => typeof(ICommandPreCommitHook<,>)
                .MakeGenericType(typeof(Query), typeof(Result<string>)))
            .Should().Throw<ArgumentException>();

    private sealed record Command : ICommand<Result<string>>;
    private sealed record OtherCommand : ICommand<Result<string>>;
    private sealed record Query : IQuery<Result<string>>;

    private sealed class FirstHook(List<string> calls) : ICommandPreCommitHook<Command, Result<string>>
    {
        public ValueTask<Result<Unit>> BeforeCommitAsync(
            Command message, Result<string> response, CancellationToken cancellationToken)
        {
            calls.Add("first");
            return Result.Ok().AsValueTask();
        }
    }

    private sealed class SecondHook(List<string> calls) : ICommandPreCommitHook<Command, Result<string>>
    {
        public ValueTask<Result<Unit>> BeforeCommitAsync(
            Command message, Result<string> response, CancellationToken cancellationToken)
        {
            calls.Add("second");
            return Result.Ok().AsValueTask();
        }
    }

    private sealed class UnitOfWork(List<string> calls) : IUnitOfWork
    {
        public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default)
        {
            calls.Add("commit");
            return Result.Ok().AsTask();
        }

        public IUnitOfWorkScope BeginScope() => new Scope();

        private sealed class Scope : IUnitOfWorkScope
        {
            public bool IsOwner => true;
            public void Dispose() { }
        }
    }
}
