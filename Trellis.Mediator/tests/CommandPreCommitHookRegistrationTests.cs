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
    public void AddTransactionalCommandBehavior_ExistingOpenBehavior_StillRegistersStateAccessor()
    {
        var services = new ServiceCollection();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionalCommandBehavior<,>));

        services.AddTransactionalCommandBehavior();

        services.Where(descriptor => descriptor.ServiceType == typeof(CommandDispatchState))
            .Should().ContainSingle().Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.Where(descriptor => descriptor.ImplementationType == typeof(TransactionalCommandBehavior<,>))
            .Should().ContainSingle();
    }

    [Fact]
    public void AddCommandPreCommitHook_TypedBinding_RegistersSharedStateAccessor()
    {
        var services = new ServiceCollection();
        services.AddCommandPreCommitHook<Command, Result<string>, FirstHook>();
        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        first.ServiceProvider.GetRequiredService<CommandDispatchState>().Should()
            .BeSameAs(second.ServiceProvider.GetRequiredService<CommandDispatchState>());
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddCommandPreCommitHook_OpenHook_NullArgument_Throws(bool nullServices)
    {
        var act = () => CommandPreCommitHookServiceCollectionExtensions.AddCommandPreCommitHook(
            nullServices ? null! : new ServiceCollection(),
            nullServices ? typeof(OpenFirstHook<,>) : null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName(nullServices ? "services" : "hookType");
    }

    [Theory]
    [InlineData(typeof(FirstHook))]
    [InlineData(typeof(OpenFirstHook<Command, Result<string>>))]
    [InlineData(typeof(ICommandPreCommitHook<,>))]
    [InlineData(typeof(AbstractOpenHook<,>))]
    [InlineData(typeof(WrongArityHook<,,>))]
    [InlineData(typeof(ReversedHook<,>))]
    [InlineData(typeof(Dictionary<,>))]
    public void AddCommandPreCommitHook_InvalidOpenType_ThrowsWithoutChangingRegistrations(Type hookType)
    {
        var services = new ServiceCollection();

        var act = () => services.AddCommandPreCommitHook(hookType);

        act.Should().Throw<ArgumentException>().WithParameterName(nameof(hookType));
        services.Should().BeEmpty();
    }

    [Fact]
    public async Task AddCommandPreCommitHook_OpenHooks_FiltersConstraintsDeduplicatesAndPreservesOrder()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddCommandPreCommitHook(typeof(OpenFirstHook<,>));
        services.AddCommandPreCommitHook(typeof(OpenSecondHook<,>));
        services.AddCommandPreCommitHook(typeof(OpenFirstHook<,>));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<ICommandPreCommitHook<OtherCommand, Result<string>>>()
            .Should().BeEmpty();
        var hooks = scope.ServiceProvider.GetServices<ICommandPreCommitHook<Command, Result<string>>>().ToArray();
        hooks.Should().HaveCount(2);
        hooks[0].Should().BeOfType<OpenFirstHook<Command, Result<string>>>();
        hooks[1].Should().BeOfType<OpenSecondHook<Command, Result<string>>>();
        scope.ServiceProvider.GetServices<ICommandPreCommitHook<Command, Result<string>>>().First()
            .Should().BeSameAs(hooks[0]);
        using var otherScope = provider.CreateScope();
        otherScope.ServiceProvider.GetServices<ICommandPreCommitHook<Command, Result<string>>>().First()
            .Should().NotBeSameAs(hooks[0]);
        var behavior = scope.ServiceProvider.GetRequiredService<IPipelineBehavior<Command, Result<string>>>();

        var result = await behavior.Handle(new Command(),
            (_, _) => Result.Ok("done").AsValueTask(), TestContext.Current.CancellationToken);

        result.Should().HaveValue("done");
        calls.Should().Equal(["open-first", "open-second", "commit"]);
    }

    private interface IAuditedCommand;
    private sealed record Command : ICommand<Result<string>>, IAuditedCommand;
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

    private abstract class AbstractOpenHook<TMessage, TResponse> : ICommandPreCommitHook<TMessage, TResponse>
        where TMessage : ICommand<TResponse>, IAuditedCommand
        where TResponse : IResult
    {
        public abstract ValueTask<Result<Unit>> BeforeCommitAsync(
            TMessage message, TResponse response, CancellationToken cancellationToken);
    }

    private sealed class OpenFirstHook<TMessage, TResponse>(List<string> calls) : AbstractOpenHook<TMessage, TResponse>
        where TMessage : ICommand<TResponse>, IAuditedCommand
        where TResponse : IResult
    {
        public override ValueTask<Result<Unit>> BeforeCommitAsync(
            TMessage message, TResponse response, CancellationToken cancellationToken)
        {
            calls.Add("open-first");
            return Result.Ok().AsValueTask();
        }
    }

    private sealed class OpenSecondHook<TMessage, TResponse>(List<string> calls) : AbstractOpenHook<TMessage, TResponse>
        where TMessage : ICommand<TResponse>, IAuditedCommand
        where TResponse : IResult
    {
        public override ValueTask<Result<Unit>> BeforeCommitAsync(
            TMessage message, TResponse response, CancellationToken cancellationToken)
        {
            calls.Add("open-second");
            return Result.Ok().AsValueTask();
        }
    }

    private sealed class WrongArityHook<TMessage, TResponse, TExtra> : AbstractOpenHook<TMessage, TResponse>
        where TMessage : ICommand<TResponse>, IAuditedCommand
        where TResponse : IResult
    {
        public override ValueTask<Result<Unit>> BeforeCommitAsync(
            TMessage message, TResponse response, CancellationToken cancellationToken) => Result.Ok().AsValueTask();
    }

    private sealed class ReversedHook<TResponse, TMessage> : AbstractOpenHook<TMessage, TResponse>
        where TMessage : ICommand<TResponse>, IAuditedCommand
        where TResponse : IResult
    {
        public override ValueTask<Result<Unit>> BeforeCommitAsync(
            TMessage message, TResponse response, CancellationToken cancellationToken) => Result.Ok().AsValueTask();
    }

    private sealed class UnitOfWork(List<string> calls) : IUnitOfWork
    {
        private Error? _rejection;

        public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_rejection is { } rejection)
                return Result.Fail(rejection).AsTask();
            calls.Add("commit");
            return Result.Ok().AsTask();
        }

        public void MarkUncommittable(Error reason)
        {
            ArgumentNullException.ThrowIfNull(reason);
            _rejection ??= reason;
        }

        public IUnitOfWorkScope BeginScope() => new Scope();

        private sealed class Scope : IUnitOfWorkScope
        {
            public bool IsOwner => true;
            public void Dispose() { }
        }
    }
}
