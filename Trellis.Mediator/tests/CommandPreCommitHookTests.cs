namespace Trellis.Mediator.Tests;

using global::Mediator;
using Trellis.Testing;
using Unit = Trellis.Unit;

public class CommandPreCommitHookTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constructor_WithHooks_NullDependency_Throws(bool nullUnitOfWork)
    {
        var act = () => new TransactionalCommandBehavior<Command, Result<string>>(
            nullUnitOfWork ? null! : new UnitOfWork(),
            nullUnitOfWork ? [] : null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == (nullUnitOfWork ? "unitOfWork" : "preCommitHooks"));
    }

    [Fact]
    public async Task Handle_Success_AwaitsHooksInOrderBeforeCommit()
    {
        var calls = new List<string>();
        var unitOfWork = new UnitOfWork(calls);
        var command = new Command("write");
        var expected = Result.Ok("done");
        var first = new CallbackHook(async (message, response, token) =>
        {
            message.Should().BeSameAs(command);
            response.Equals(expected).Should().BeTrue();
            token.Should().Be(Token);
            unitOfWork.ScopeDepth.Should().Be(1);
            calls.Add("first-start");
            await Task.Yield();
            calls.Add("first-end");
            return Result.Ok();
        });
        var second = new CallbackHook((_, _, _) =>
        {
            calls.Add("second");
            return Result.Ok().AsValueTask();
        });
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(unitOfWork, [first, second]);

        var result = await behavior.Handle(command, (_, _) =>
        {
            calls.Add("handler");
            return expected.AsValueTask();
        }, Token);

        result.Equals(expected).Should().BeTrue();
        calls.Should().Equal(["handler", "first-start", "first-end", "second", "commit"]);
        unitOfWork.ScopeDepth.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Handle_HookRejects_StopsRemainingHooksAndVetoesCommit(
        bool persistHandlerFailure, bool persistHookFailure)
    {
        var calls = new List<string>();
        var unitOfWork = new UnitOfWork(calls);
        var rejection = new Error.Unexpected("audit.not-recorded");
        var hookFailure = persistHookFailure ? Result.FailAfterCommit(rejection) : Result.Fail(rejection);
        var hooks = new[]
        {
            new CallbackHook((_, _, _) =>
            {
                calls.Add("first");
                return Result.Ok().AsValueTask();
            }),
            new CallbackHook((_, _, _) =>
            {
                calls.Add("reject");
                return hookFailure.AsValueTask();
            }),
            new CallbackHook((_, _, _) =>
            {
                calls.Add("unreachable");
                return Result.Ok().AsValueTask();
            }),
        };
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(unitOfWork, hooks);
        var response = persistHandlerFailure
            ? Result.FailAfterCommit<string>(new Error.Unavailable { Code = "worker.failed" })
            : Result.Ok("done");

        var result = await behavior.Handle(new Command("write"), (_, _) => response.AsValueTask(), Token);

        result.Should().BeFailure().Which.Should().BeSameAs(rejection);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse();
        calls.Should().Equal(["first", "reject"]);
        unitOfWork.CommitCalls.Should().Be(0);
        unitOfWork.ScopeDepth.Should().Be(0);
    }

    [Fact]
    public async Task Handle_OrdinaryHandlerFailure_SkipsHooksAndCommit()
    {
        var unitOfWork = new UnitOfWork();
        var hook = new CallbackHook((_, _, _) => throw new InvalidOperationException("Hook must not run."));
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(unitOfWork, [hook]);
        var expected = Result.Fail<string>(new Error.Conflict(Code: "command.rejected"));

        var result = await behavior.Handle(new Command("write"), (_, _) => expected.AsValueTask(), Token);

        result.Equals(expected).Should().BeTrue();
        unitOfWork.CommitCalls.Should().Be(0);
        unitOfWork.ScopeDepth.Should().Be(0);
        (await unitOfWork.CommitAsync(Token)).Should().BeSuccess();
    }

    [Fact]
    public async Task Handle_PersistOnFailure_HooksReceiveOriginalFailureAndCommit()
    {
        var unitOfWork = new UnitOfWork();
        var expected = Result.FailAfterCommit<string>(new Error.Unavailable { Code = "worker.failed" });
        var hookCalls = 0;
        var hook = new CallbackHook((_, response, _) =>
        {
            response.Equals(expected).Should().BeTrue();
            hookCalls++;
            return Result.Ok().AsValueTask();
        });
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(unitOfWork, [hook]);

        var result = await behavior.Handle(new Command("write"), (_, _) => expected.AsValueTask(), Token);

        result.Equals(expected).Should().BeTrue();
        hookCalls.Should().Be(1);
        unitOfWork.SaveAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Handle_CommitFailsAfterHooks_ReturnsCommitFailure()
    {
        var calls = new List<string>();
        var error = new Error.Conflict(Code: FaultCodes.ConcurrentModification);
        var unitOfWork = new UnitOfWork(calls) { CommitResult = Result.Fail(error) };
        var hook = new CallbackHook((_, _, _) =>
        {
            calls.Add("hook");
            return Result.Ok().AsValueTask();
        });
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(unitOfWork, [hook]);

        var result = await behavior.Handle(new Command("write"), (_, _) => Result.Ok("done").AsValueTask(), Token);

        result.Should().BeFailure().Which.Should().BeSameAs(error);
        calls.Should().Equal(["hook", "commit"]);
    }

    [Fact]
    public async Task Handle_HookThrows_PropagatesAndDoesNotCommit()
    {
        var unitOfWork = new UnitOfWork();
        var exception = new InvalidOperationException("Hook failed.");
        var hook = new CallbackHook((_, _, _) => throw exception);
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(unitOfWork, [hook]);

        var act = async () => await behavior.Handle(new Command("write"),
            (_, _) => Result.Ok("done").AsValueTask(), Token);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
        unitOfWork.CommitCalls.Should().Be(0);
        unitOfWork.ScopeDepth.Should().Be(0);
    }

    [Fact]
    public async Task Handle_HookObservesCancellation_PropagatesAndDoesNotCommit()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await source.CancelAsync();
        var unitOfWork = new UnitOfWork();
        var hook = new CallbackHook((_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Result.Ok().AsValueTask();
        });
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(unitOfWork, [hook]);

        var act = async () => await behavior.Handle(new Command("write"),
            (_, _) => Result.Ok("done").AsValueTask(), source.Token);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(source.Token);
        unitOfWork.CommitCalls.Should().Be(0);
        unitOfWork.ScopeDepth.Should().Be(0);
    }

    [Fact]
    public async Task Handle_HandlerThrows_SkipsHooksAndDisposesScope()
    {
        var unitOfWork = new UnitOfWork();
        var hook = new CallbackHook((_, _, _) => throw new InvalidOperationException("Hook must not run."));
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(unitOfWork, [hook]);

        var act = async () => await behavior.Handle(new Command("write"),
            (_, _) => throw new InvalidOperationException("Handler failed."), Token);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Handler failed.");
        unitOfWork.CommitCalls.Should().Be(0);
        unitOfWork.ScopeDepth.Should().Be(0);
        (await unitOfWork.CommitAsync(Token)).Should().BeSuccess();
    }

    [Theory]
    [InlineData("veto", false)]
    [InlineData("veto", true)]
    [InlineData("throw", false)]
    [InlineData("throw", true)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    public async Task Handle_IgnoredNestedHookFailure_BlocksOwningAndLaterCommits(
        string failure, bool persistOuterFailure)
    {
        var unitOfWork = new UnitOfWork();
        var hook = new CallbackHook((message, _, token) =>
        {
            if (message.Name != "inner")
                return Result.Ok().AsValueTask();
            return failure switch
            {
                "veto" => Result.FailAfterCommit(new Error.Conflict("audit.rejected")).AsValueTask(),
                "throw" => throw new InvalidOperationException("Hook failed."),
                "cancel" => throw new OperationCanceledException(token),
                _ => throw new ArgumentOutOfRangeException(nameof(failure)),
            };
        });
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(unitOfWork, [hook]);

        var result = await behavior.Handle(new Command("outer"), async (_, token) =>
        {
            ValueTask<Result<string>> Inner() => behavior.Handle(new Command("inner"),
                (_, _) => Result.Ok("inner").AsValueTask(), token);
            if (failure == "veto")
                (await Inner()).Should().HaveErrorCode("audit.rejected");
            else
            {
                var act = async () => await Inner();
                if (failure == "throw")
                    await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Hook failed.");
                else
                    await act.Should().ThrowAsync<OperationCanceledException>();
            }

            return persistOuterFailure
                ? Result.FailAfterCommit<string>(new Error.Conflict("outer.failed"))
                : Result.Ok("outer");
        }, Token);

        var expectedCode = failure == "veto" ? "audit.rejected" : FaultCodes.UnitOfWorkUncommittable;
        result.Should().HaveErrorCode(expectedCode);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse();
        (await unitOfWork.CommitAsync(Token)).Should().HaveErrorCode(expectedCode);
        using (unitOfWork.BeginScope())
            (await unitOfWork.CommitAsync(Token)).Should().HaveErrorCode(expectedCode);
        unitOfWork.SaveAttempts.Should().Be(0);
        unitOfWork.ScopeDepth.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_NestedCommands_RunsEachDispatchHookBeforeItsDeferredCommit(bool outerSucceeds)
    {
        var unitOfWork = new UnitOfWork();
        var hookedCommands = new List<string>();
        var hook = new CallbackHook((message, _, _) =>
        {
            hookedCommands.Add(message.Name);
            unitOfWork.SaveAttempts.Should().Be(0);
            return Result.Ok().AsValueTask();
        });
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(unitOfWork, [hook]);

        var result = await behavior.Handle(new Command("outer"), async (_, token) =>
        {
            var inner = await behavior.Handle(new Command("inner"),
                (_, _) => Result.Ok("inner").AsValueTask(), token);
            inner.Should().BeSuccess();
            unitOfWork.SaveAttempts.Should().Be(0);
            return outerSucceeds
                ? Result.Ok("outer")
                : Result.Fail<string>(new Error.Conflict(Code: "outer.rejected"));
        }, Token);

        result.IsSuccess.Should().Be(outerSucceeds);
        hookedCommands.Should().Equal(outerSucceeds ? ["inner", "outer"] : ["inner"]);
        unitOfWork.CommitCalls.Should().Be(outerSucceeds ? 2 : 1);
        unitOfWork.SaveAttempts.Should().Be(outerSucceeds ? 1 : 0);
        unitOfWork.ScopeDepth.Should().Be(0);
    }

    [Fact]
    public async Task Handle_SequentialCommands_ReusesHooksWithEachDispatchOutcome()
    {
        var unitOfWork = new UnitOfWork();
        var hookedCommands = new List<string>();
        var hook = new CallbackHook((message, response, _) =>
        {
            response.Should().HaveValue(message.Name);
            hookedCommands.Add(message.Name);
            return Result.Ok().AsValueTask();
        });
        var behavior = new TransactionalCommandBehavior<Command, Result<string>>(unitOfWork, [hook]);

        foreach (var name in new[] { "first", "second" })
            (await behavior.Handle(new Command(name),
                (message, _) => Result.Ok(message.Name).AsValueTask(), Token)).Should().BeSuccess();

        hookedCommands.Should().Equal(["first", "second"]);
        unitOfWork.SaveAttempts.Should().Be(2);
    }

    private sealed record Command(string Name) : ICommand<Result<string>>;

    private sealed class CallbackHook(
        Func<Command, Result<string>, CancellationToken, ValueTask<Result<Unit>>> callback)
        : ICommandPreCommitHook<Command, Result<string>>
    {
        public ValueTask<Result<Unit>> BeforeCommitAsync(
            Command message, Result<string> response, CancellationToken cancellationToken) =>
            callback(message, response, cancellationToken);
    }

    private sealed class UnitOfWork(List<string>? calls = null) : IUnitOfWork
    {
        private Error? _rejection;
        public int ScopeDepth { get; private set; }
        public int CommitCalls { get; private set; }
        public int SaveAttempts { get; private set; }
        public Result<Unit> CommitResult { get; init; } = Result.Ok();

        public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            if (_rejection is { } rejection)
                return Result.Fail(rejection).AsTask();
            if (ScopeDepth > 1)
                return Result.Ok().AsTask();

            SaveAttempts++;
            calls?.Add("commit");
            return CommitResult.AsTask();
        }

        public void MarkUncommittable(Error reason)
        {
            ArgumentNullException.ThrowIfNull(reason);
            _rejection ??= reason;
        }

        public IUnitOfWorkScope BeginScope()
        {
            ScopeDepth++;
            return new Scope(this, ScopeDepth == 1);
        }

        private sealed class Scope(UnitOfWork owner, bool isOwner) : IUnitOfWorkScope
        {
            private bool _disposed;
            public bool IsOwner { get; } = isOwner;

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                owner.ScopeDepth--;
            }
        }
    }
}
