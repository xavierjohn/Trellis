namespace Trellis.Mediator.Tests;

using global::Mediator;
using Trellis.Testing;
using Unit = Trellis.Unit;

public class CommandDispatchStateTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("get")]
    [InlineData("try-get")]
    [InlineData("remove")]
    public void State_OutsideTransactionalDispatch_Throws(string operation)
    {
        var state = new CommandDispatchState();
        Action act = operation switch
        {
            "get" => () => state.GetOrAdd(static () => new Pending()),
            "try-get" => () => state.TryGet<Pending>(out _),
            "remove" => () => state.Remove<Pending>(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*transactional*command*");
    }

    [Fact]
    public async Task GetOrAdd_ActiveDispatch_InvokesFactoryOnceAndSharesEntryWithHook()
    {
        var state = new CommandDispatchState();
        var factoryCalls = 0;
        Pending? recorded = null;
        var hook = new Hook((_, _, _) =>
        {
            state.TryGet<Pending>(out var pending).Should().BeTrue();
            pending.Should().BeSameAs(recorded);
            pending!.Items.Should().Equal(["audit"]);
            state.Remove<Pending>().Should().BeTrue();
            state.Remove<Pending>().Should().BeFalse();
            state.TryGet<Pending>(out _).Should().BeFalse();
            state.TryGet<OtherPending>(out _).Should().BeTrue();
            return Result.Ok().AsValueTask();
        });
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(new UnitOfWork(), [hook]);

        var result = await behavior.Handle(new Command(), (_, _) =>
        {
            state.TryGet<Pending>(out _).Should().BeFalse();
            recorded = state.GetOrAdd(() =>
            {
                factoryCalls++;
                return new Pending();
            });
            recorded.Items.Add("audit");
            state.GetOrAdd<Pending>(() => throw new InvalidOperationException("Factory must not run."))
                .Should().BeSameAs(recorded);
            state.GetOrAdd(static () => new OtherPending());
            return Result.Ok("done").AsValueTask();
        }, Token);

        result.Should().HaveValue("done");
        factoryCalls.Should().Be(1);
        FluentActions.Invoking(() => state.TryGet<OtherPending>(out _))
            .Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetOrAdd_InvalidFactory_ThrowsWithoutAddingEntry(bool nullFactory)
    {
        var state = new CommandDispatchState();
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(new UnitOfWork());

        var result = await behavior.Handle(new Command(), (_, _) =>
        {
            var act = () => state.GetOrAdd<Pending>(nullFactory ? null! : static () => null!);
            if (nullFactory)
                act.Should().Throw<ArgumentNullException>().WithParameterName("factory");
            else
                act.Should().Throw<InvalidOperationException>().WithMessage("*null*");
            state.TryGet<Pending>(out _).Should().BeFalse();
            return Result.Ok("done").AsValueTask();
        }, Token);

        result.Should().BeSuccess();
    }

    [Fact]
    public async Task Handle_SameMessageInstanceInNestedDispatch_IsolatesAndRestoresEntries()
    {
        var state = new CommandDispatchState();
        var command = new Command();
        Pending? outer = null;
        var observed = new List<string>();
        var hook = new Hook((message, response, _) =>
        {
            message.Should().BeSameAs(command);
            var expected = response.Unwrap();
            state.TryGet<Pending>(out var pending).Should().BeTrue();
            pending!.Items.Should().Equal([expected]);
            observed.Add(expected);
            state.Remove<Pending>().Should().BeTrue();
            return Result.Ok().AsValueTask();
        });
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(new UnitOfWork(), [hook]);

        var result = await behavior.Handle(command, async (outerCommand, token) =>
        {
            outer = state.GetOrAdd(static () => new Pending());
            outer.Items.Add("outer");
            var inner = await behavior.Handle(command, (_, _) =>
            {
                state.TryGet<Pending>(out _).Should().BeFalse();
                var pending = state.GetOrAdd(static () => new Pending());
                pending.Should().NotBeSameAs(outer);
                pending.Items.Add("inner");
                return Result.Ok("inner").AsValueTask();
            }, token);
            inner.Should().HaveValue("inner");
            state.TryGet<Pending>(out var restored).Should().BeTrue();
            restored.Should().BeSameAs(outer);
            return Result.Ok("outer");
        }, Token);

        result.Should().HaveValue("outer");
        observed.Should().Equal(["inner", "outer"]);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("handler-failure")]
    [InlineData("handler-throw")]
    [InlineData("hook-failure")]
    [InlineData("hook-throw")]
    [InlineData("hook-cancel")]
    [InlineData("commit-failure")]
    [InlineData("persist-failure")]
    public async Task Handle_EveryExit_ClearsEntriesBeforeSequentialReuseOfMessage(string outcome)
    {
        var state = new CommandDispatchState();
        var message = new Command();
        var hook = new Hook((_, _, token) => outcome switch
        {
            "hook-failure" => Result.Fail(new Error.Conflict("audit.rejected")).AsValueTask(),
            "hook-throw" => throw new InvalidOperationException("Hook failed."),
            "hook-cancel" => throw new OperationCanceledException(token),
            _ => Result.Ok().AsValueTask(),
        });
        var unitOfWork = new UnitOfWork
        {
            CommitResult = outcome == "commit-failure"
                ? Result.Fail(new Error.Conflict("commit.rejected"))
                : Result.Ok(),
        };
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(unitOfWork, [hook]);
        ValueTask<Result<string>> Invoke() => behavior.Handle(message, (_, _) =>
        {
            state.GetOrAdd(static () => new Pending()).Items.Add("first");
            return outcome switch
            {
                "handler-failure" => Result.Fail<string>(new Error.Conflict("handler.rejected")).AsValueTask(),
                "handler-throw" => throw new InvalidOperationException("Handler failed."),
                "persist-failure" => Result.FailAfterCommit<string>(new Error.Conflict("worker.failed")).AsValueTask(),
                _ => Result.Ok("first").AsValueTask(),
            };
        }, Token);

        if (outcome is "handler-throw" or "hook-throw" or "hook-cancel")
        {
            var act = async () => await Invoke();
            if (outcome == "hook-cancel")
                await act.Should().ThrowAsync<OperationCanceledException>();
            else
                await act.Should().ThrowAsync<InvalidOperationException>();
        }
        else
            (await Invoke()).IsSuccess.Should().Be(outcome == "success");

        FluentActions.Invoking(() => state.TryGet<Pending>(out _))
            .Should().Throw<InvalidOperationException>();
        var next = new TransactionalCommandBehavior<Command, Result<string>>(new UnitOfWork());
        var result = await next.Handle(message, (_, _) =>
        {
            state.TryGet<Pending>(out _).Should().BeFalse();
            state.GetOrAdd(static () => new Pending()).Items.Add("second");
            return Result.Ok("second").AsValueTask();
        }, Token);
        result.Should().HaveValue("second");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_FailedNestedHandler_RestoresParentEntries(bool throws)
    {
        var state = new CommandDispatchState();
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(new UnitOfWork());

        var result = await behavior.Handle(new Command(), async (_, token) =>
        {
            var outer = state.GetOrAdd(static () => new Pending());
            outer.Items.Add("outer");
            ValueTask<Result<string>> Inner() => behavior.Handle(new Command(), (_, _) =>
            {
                state.GetOrAdd(static () => new Pending()).Items.Add("inner");
                return throws
                    ? throw new InvalidOperationException("Inner failed.")
                    : Result.Fail<string>(new Error.Conflict("inner.failed")).AsValueTask();
            }, token);
            if (throws)
            {
                var act = async () => await Inner();
                await act.Should().ThrowAsync<InvalidOperationException>();
            }
            else
                (await Inner()).Should().BeFailure();
            state.TryGet<Pending>(out var restored).Should().BeTrue();
            restored.Should().BeSameAs(outer);
            restored!.Items.Should().Equal(["outer"]);
            return Result.Ok("outer");
        }, Token);

        result.Should().HaveValue("outer");
    }

    [Fact]
    public async Task Handle_StructCommand_ProvidesDispatchStateWithoutMessageIdentity()
    {
        var state = new CommandDispatchState();
        var behavior = new TransactionalCommandBehavior<StructCommand, Result<string>>(new UnitOfWork());

        var result = await behavior.Handle(new StructCommand(), (_, _) =>
        {
            state.GetOrAdd(static () => new Pending()).Items.Add("struct");
            state.TryGet<Pending>(out var pending).Should().BeTrue();
            pending!.Items.Should().Equal(["struct"]);
            return Result.Ok("done").AsValueTask();
        }, Token);

        result.Should().BeSuccess();
    }

    [Fact]
    public async Task Handle_OrphanTask_AccessToExpiredFrameThrows()
    {
        var state = new CommandDispatchState();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? orphan = null;
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(new UnitOfWork());

        try
        {
            var result = await behavior.Handle(new Command(), (_, _) =>
            {
                state.GetOrAdd(static () => new Pending());
                orphan = Task.Run(async () =>
                {
                    await release.Task.WaitAsync(Token);
                    FluentActions.Invoking(() => state.TryGet<Pending>(out _))
                        .Should().Throw<InvalidOperationException>().WithMessage("*expired*");
                }, Token);
                return Result.Ok("done").AsValueTask();
            }, Token);
            result.Should().BeSuccess();
        }
        finally
        {
            release.TrySetResult();
        }

        orphan.Should().NotBeNull();
        await orphan!;
    }

    [Fact]
    public async Task Handle_ParallelCommandsWithSeparateUnitsOfWork_IsolatesAmbientFrames()
    {
        var state = new CommandDispatchState();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        async Task<Result<string>> Run(string name)
        {
            var behavior = new TransactionalCommandBehavior<Command, Result<string>>(new UnitOfWork());
            return await behavior.Handle(new Command(), async (_, _) =>
            {
                var pending = state.GetOrAdd(static () => new Pending());
                pending.Items.Add(name);
                if (Interlocked.Increment(ref entered) == 2)
                    ready.TrySetResult();
                await ready.Task.WaitAsync(Token);
                state.TryGet<Pending>(out var current).Should().BeTrue();
                current.Should().BeSameAs(pending);
                current!.Items.Should().Equal([name]);
                return Result.Ok(name);
            }, Token);
        }

        var results = await Task.WhenAll(Run("first"), Run("second"));

        results.Select(result => result.Unwrap()).Should().Equal(["first", "second"]);
    }

    private sealed class Pending
    {
        public List<string> Items { get; } = [];
    }

    private sealed class OtherPending;
    private sealed record Command : ICommand<Result<string>>;
    private readonly record struct StructCommand : ICommand<Result<string>>;

    private sealed class Hook(
        Func<Command, Result<string>, CancellationToken, ValueTask<Result<Unit>>> callback)
        : ICommandPreCommitHook<Command, Result<string>>
    {
        public ValueTask<Result<Unit>> BeforeCommitAsync(
            Command message, Result<string> response, CancellationToken cancellationToken) =>
            callback(message, response, cancellationToken);
    }

    private sealed class UnitOfWork : IUnitOfWork
    {
        private int _depth;
        private Error? _rejection;
        public Result<Unit> CommitResult { get; init; } = Result.Ok();
        public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default) =>
            (_rejection is { } rejection ? Result.Fail(rejection) : _depth > 1 ? Result.Ok() : CommitResult).AsTask();
        public void MarkUncommittable(Error reason)
        {
            ArgumentNullException.ThrowIfNull(reason);
            _rejection ??= reason;
        }
        public IUnitOfWorkScope BeginScope()
        {
            _depth++;
            return new Scope(this, _depth == 1);
        }

        private sealed class Scope(UnitOfWork owner, bool isOwner) : IUnitOfWorkScope
        {
            public bool IsOwner { get; } = isOwner;
            public void Dispose() => owner._depth--;
        }
    }
}
