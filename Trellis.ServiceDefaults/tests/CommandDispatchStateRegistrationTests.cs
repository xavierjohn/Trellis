namespace Trellis.ServiceDefaults.Tests;

using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;
using Unit = Trellis.Unit;

public class CommandDispatchStateRegistrationTests
{
    [Theory]
    [InlineData("transaction")]
    [InlineData("existing-open")]
    [InlineData("typed-hook")]
    [InlineData("open-hook")]
    [InlineData("ef")]
    [InlineData("builder-typed")]
    [InlineData("builder-open")]
    [InlineData("builder-ef")]
    [InlineData("closed-type")]
    [InlineData("closed-factory")]
    public async Task Registration_StateDependentHandlerAndHook_SharesSingleAccessor(string registration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICommandHandler<Command, Result<string>>, Handler>();
        switch (registration)
        {
            case "transaction":
                services.AddTransactionalCommandBehavior();
                break;
            case "existing-open":
                services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionalCommandBehavior<,>));
                services.AddTransactionalCommandBehavior();
                break;
            case "typed-hook":
                services.AddCommandPreCommitHook<Command, Result<string>, Hook<Command, Result<string>>>();
                break;
            case "open-hook":
                services.AddCommandPreCommitHook(typeof(Hook<,>));
                break;
            case "ef":
                services.AddTrellisUnitOfWork<TestDbContext>();
                break;
            case "builder-typed":
                services.AddTrellis(options =>
                    options.UseCommandPreCommitHook<Command, Result<string>, Hook<Command, Result<string>>>());
                break;
            case "builder-open":
                services.AddTrellis(options => options.UseCommandPreCommitHook(typeof(Hook<,>)));
                break;
            case "builder-ef":
                services.AddTrellis(options => options.UseEntityFrameworkUnitOfWork<TestDbContext>());
                break;
            case "closed-type":
                services.AddTrellisUnitOfWorkWithoutBehavior<TestDbContext>();
                services.AddSingleton<CommandDispatchState>();
                services.AddScoped<IPipelineBehavior<Command, Result<string>>,
                    TransactionalCommandBehavior<Command, Result<string>>>();
                break;
            case "closed-factory":
                services.AddTrellisUnitOfWorkWithoutBehavior<TestDbContext>();
                services.AddSingleton<CommandDispatchState>();
                services.AddScoped<IPipelineBehavior<Command, Result<string>>>(provider =>
                    new TransactionalCommandBehavior<Command, Result<string>>(
                        provider.GetRequiredService<IUnitOfWork>(),
                        provider.GetServices<ICommandPreCommitHook<Command, Result<string>>>()));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(registration));
        }

        if (registration is not ("typed-hook" or "open-hook" or "builder-typed" or "builder-open"))
            services.AddScoped<ICommandPreCommitHook<Command, Result<string>>, Hook<Command, Result<string>>>();

        services.Where(descriptor => descriptor.ServiceType == typeof(CommandDispatchState))
            .Should().ContainSingle().Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();
        var state = scope.ServiceProvider.GetRequiredService<CommandDispatchState>();
        otherScope.ServiceProvider.GetRequiredService<CommandDispatchState>().Should().BeSameAs(state);
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Command, Result<string>>>();
        var transaction = scope.ServiceProvider.GetServices<IPipelineBehavior<Command, Result<string>>>()
            .OfType<TransactionalCommandBehavior<Command, Result<string>>>().Should().ContainSingle().Which;

        var result = await transaction.Handle(new Command(), handler.Handle, TestContext.Current.CancellationToken);

        result.TryGetValue(out var value).Should().BeTrue();
        value.Should().Be("recorded");
        FluentActions.Invoking(() => state.TryGet<Pending>(out _))
            .Should().Throw<InvalidOperationException>();
    }

    private sealed record Command : ICommand<Result<string>>;

    private sealed class Pending
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class Handler(CommandDispatchState state) : ICommandHandler<Command, Result<string>>
    {
        public ValueTask<Result<string>> Handle(Command message, CancellationToken cancellationToken)
        {
            state.GetOrAdd(static () => new Pending()).Value = "recorded";
            return Result.Ok("recorded").AsValueTask();
        }
    }

    private sealed class Hook<TMessage, TResponse>(CommandDispatchState state)
        : ICommandPreCommitHook<TMessage, TResponse>
        where TMessage : ICommand<TResponse>
        where TResponse : IResult
    {
        public ValueTask<Result<Unit>> BeforeCommitAsync(
            TMessage message, TResponse response, CancellationToken cancellationToken)
        {
            state.TryGet<Pending>(out var pending).Should().BeTrue();
            pending!.Value.Should().Be("recorded");
            state.Remove<Pending>().Should().BeTrue();
            return Result.Ok().AsValueTask();
        }
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);

    private sealed class UnitOfWork : IUnitOfWork
    {
        private Error? _rejection;

        public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default) =>
            (_rejection is { } rejection ? Result.Fail(rejection) : Result.Ok()).AsTask();

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
