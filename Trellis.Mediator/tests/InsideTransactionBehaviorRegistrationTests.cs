namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Testing;
using Unit = Trellis.Unit;

public class InsideTransactionBehaviorRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddInsideTransactionBehavior_DuplicateBindings_NestsOnceBeforeCommit(bool open)
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        if (open)
        {
            services.AddInsideTransactionBehavior(typeof(FirstBehavior<,>));
            services.AddInsideTransactionBehavior(typeof(SecondBehavior<,>));
            services.AddInsideTransactionBehavior(typeof(FirstBehavior<,>));
        }
        else
        {
            services.AddInsideTransactionBehavior<Command, Result<string>, FirstBehavior<Command, Result<string>>>();
            services.AddInsideTransactionBehavior<Command, Result<string>, SecondBehavior<Command, Result<string>>>();
            services.AddInsideTransactionBehavior<Command, Result<string>, FirstBehavior<Command, Result<string>>>();
        }

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        using var scope = provider.CreateScope();
        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<Command, Result<string>>>().ToArray();

        var result = await Invoke(behaviors, (_, _) =>
        {
            calls.Add("handler");
            return Result.Ok("done").AsValueTask();
        });

        result.Should().HaveValue("done");
        calls.Should().Equal(["first:before", "second:before", "handler", "second:after", "first:after", "commit"]);
        behaviors.Should().HaveCount(3);
        scope.ServiceProvider.GetServices<IPipelineBehavior<Command, Result<string>>>().Last()
            .Should().BeSameAs(behaviors[^1]);
        using var otherScope = provider.CreateScope();
        otherScope.ServiceProvider.GetServices<IPipelineBehavior<Command, Result<string>>>().Last()
            .Should().NotBeSameAs(behaviors[^1]);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("exception")]
    [InlineData("persist-failure")]
    public async Task AddInsideTransactionBehavior_HandlerOutcome_PreservesCommitAndUnwindSemantics(string outcome)
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddInsideTransactionBehavior(typeof(FirstBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<Command, Result<string>>>().ToArray();
        var error = new Error.Conflict("command.rejected");
        var original = new InvalidOperationException("Handler failed.");
        async Task<Result<string>> Act() => await Invoke(behaviors, async (_, _) =>
        {
            await Task.Yield();
            calls.Add("handler");
            if (outcome == "exception")
                throw original;
            return outcome == "persist-failure"
                ? Result.FailAfterCommit<string>(error)
                : Result.Fail<string>(error);
        });

        if (outcome == "exception")
        {
            var act = () => Act();
            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(original);
            calls.Should().Equal(["first:before", "handler"]);
        }
        else
        {
            (await Act()).UnwrapError().Should().BeSameAs(error);
            calls.Should().Equal(outcome == "persist-failure"
                ? ["first:before", "handler", "first:after", "commit"]
                : ["first:before", "handler", "first:after"]);
        }
    }

    [Fact]
    public void AddInsideTransactionBehavior_OpenBinding_FiltersUnmarkedCommandsAndQueries()
    {
        var services = new ServiceCollection();
        services.AddSingleton<List<string>>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddInsideTransactionBehavior(typeof(FirstBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IPipelineBehavior<OtherCommand, Result<string>>>()
            .Should().ContainSingle().Which.Should().BeOfType<TransactionalCommandBehavior<OtherCommand, Result<string>>>();
        scope.ServiceProvider.GetServices<IPipelineBehavior<Query, Result<string>>>().Should().BeEmpty();
    }

    [Fact]
    public void AddInsideTransactionBehavior_MissingUnitOfWork_FailsResolution()
    {
        var services = new ServiceCollection();
        services.AddSingleton<List<string>>();
        services.AddInsideTransactionBehavior(typeof(FirstBehavior<,>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetServices<IPipelineBehavior<Command, Result<string>>>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*IUnitOfWork*");
    }

    [Fact]
    public void AddInsideTransactionBehavior_TypedBinding_NullServices_Throws() =>
        FluentActions.Invoking(() => InsideTransactionBehaviorServiceCollectionExtensions
                .AddInsideTransactionBehavior<Command, Result<string>, FirstBehavior<Command, Result<string>>>(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddInsideTransactionBehavior_OpenBinding_NullArgument_Throws(bool nullServices)
    {
        var act = () => InsideTransactionBehaviorServiceCollectionExtensions.AddInsideTransactionBehavior(
            nullServices ? null! : new ServiceCollection(), nullServices ? typeof(FirstBehavior<,>) : null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName(nullServices ? "services" : "behaviorType");
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(IPipelineBehavior<,>))]
    [InlineData(typeof(AbstractBehavior<,>))]
    [InlineData(typeof(FirstBehavior<Command, Result<string>>))]
    [InlineData(typeof(ReversedBehavior<,>))]
    [InlineData(typeof(UnconstrainedBehavior<,>))]
    [InlineData(typeof(MissingFailureFactoryBehavior<,>))]
    public void AddInsideTransactionBehavior_InvalidOpenType_ThrowsWithoutChangingServices(Type behaviorType)
    {
        var services = new ServiceCollection();

        var act = () => services.AddInsideTransactionBehavior(behaviorType);

        act.Should().Throw<ArgumentException>().WithParameterName(nameof(behaviorType));
        services.Should().BeEmpty();
    }

    [Fact]
    public void AddInsideTransactionBehavior_ClosedTransactionConflict_PreservesExistingRegistrations()
    {
        var services = new ServiceCollection();
        services.AddScoped<IPipelineBehavior<Command, Result<string>>, TransactionalCommandBehavior<Command, Result<string>>>();
        var original = services.ToArray();

        var act = () => services.AddInsideTransactionBehavior(typeof(FirstBehavior<,>));

        act.Should().Throw<InvalidOperationException>();
        services.Should().Equal(original);
    }

    private static ValueTask<Result<string>> Invoke(
        IEnumerable<IPipelineBehavior<Command, Result<string>>> behaviors,
        MessageHandlerDelegate<Command, Result<string>> handler)
    {
        var next = handler;
        foreach (var behavior in behaviors.Reverse())
        {
            var inner = next;
            next = (message, token) => behavior.Handle(message, inner, token);
        }

        return next(new Command(), TestContext.Current.CancellationToken);
    }

    private interface IRecordedCommand;
    private sealed record Command : ICommand<Result<string>>, IRecordedCommand;
    private sealed record OtherCommand : ICommand<Result<string>>;
    private sealed record Query : IQuery<Result<string>>;

    private abstract class AbstractBehavior<TMessage, TResponse>(List<string> calls, string name)
        : IPipelineBehavior<TMessage, TResponse>
        where TMessage : ICommand<TResponse>, IRecordedCommand
        where TResponse : IResult, IFailureFactory<TResponse>
    {
        public async ValueTask<TResponse> Handle(
            TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
        {
            calls.Add($"{name}:before");
            var response = await next(message, cancellationToken);
            calls.Add($"{name}:after");
            return response;
        }
    }

    private sealed class FirstBehavior<TMessage, TResponse>(List<string> calls)
        : AbstractBehavior<TMessage, TResponse>(calls, "first")
        where TMessage : ICommand<TResponse>, IRecordedCommand
        where TResponse : IResult, IFailureFactory<TResponse>;

    private sealed class SecondBehavior<TMessage, TResponse>(List<string> calls)
        : AbstractBehavior<TMessage, TResponse>(calls, "second")
        where TMessage : ICommand<TResponse>, IRecordedCommand
        where TResponse : IResult, IFailureFactory<TResponse>;

    private sealed class ReversedBehavior<TResponse, TMessage>(List<string> calls)
        : AbstractBehavior<TMessage, TResponse>(calls, "reversed")
        where TMessage : ICommand<TResponse>, IRecordedCommand
        where TResponse : IResult, IFailureFactory<TResponse>;

    private sealed class UnconstrainedBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public ValueTask<TResponse> Handle(
            TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken) =>
            next(message, cancellationToken);
    }

    private sealed class MissingFailureFactoryBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : ICommand<TResponse>
        where TResponse : IResult
    {
        public ValueTask<TResponse> Handle(
            TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken) =>
            next(message, cancellationToken);
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