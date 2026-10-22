namespace Trellis.Testing.Tests.Fakes;

public class FakeUnitOfWorkTests
{
    [Fact]
    public void Constructor_Default_StartsWithSuccessAndZeroCounts()
    {
        var unitOfWork = new FakeUnitOfWork();

        unitOfWork.CommitResult.Should().BeSuccess();
        unitOfWork.CommitCallCount.Should().Be(0);
        unitOfWork.CommitAttemptCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitAsync_Default_WithOrWithoutScope_ReturnsSuccessAndCountsAttempt(bool openScope)
    {
        var unitOfWork = new FakeUnitOfWork();
        using var scope = openScope ? unitOfWork.BeginScope() : null;

        var result = await unitOfWork.CommitAsync(TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        unitOfWork.CommitCallCount.Should().Be(1);
        unitOfWork.CommitAttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task CommitAsync_ConfiguredFailure_ReturnsOriginalErrorAndCountsAttempt()
    {
        var error = new Error.Conflict("test.commit-failed");
        var unitOfWork = new FakeUnitOfWork { CommitResult = Result.Fail(error) };
        using var scope = unitOfWork.BeginScope();

        var result = await unitOfWork.CommitAsync(TestContext.Current.CancellationToken);

        result.Should().BeFailureOfType<Error.Conflict>().Which.Should().BeSameAs(error);
        unitOfWork.CommitCallCount.Should().Be(1);
        unitOfWork.CommitAttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task CommitAsync_NestedScope_DefersConfiguredFailureUntilOwnerCommit()
    {
        var error = new Error.Conflict("test.commit-failed");
        var unitOfWork = new FakeUnitOfWork { CommitResult = Result.Fail(error) };
        using var owner = unitOfWork.BeginScope();
        using (unitOfWork.BeginScope())
        {
            var deferred = await unitOfWork.CommitAsync(TestContext.Current.CancellationToken);

            deferred.Should().BeSuccess();
            unitOfWork.CommitCallCount.Should().Be(1);
            unitOfWork.CommitAttemptCount.Should().Be(0);
        }

        var result = await unitOfWork.CommitAsync(TestContext.Current.CancellationToken);

        result.Should().BeFailureOfType<Error.Conflict>().Which.Should().BeSameAs(error);
        unitOfWork.CommitCallCount.Should().Be(2);
        unitOfWork.CommitAttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task CommitAsync_ThreeScopes_DefersUntilOnlyOwnerRemains()
    {
        var unitOfWork = new FakeUnitOfWork();
        using var owner = unitOfWork.BeginScope();
        using var middle = unitOfWork.BeginScope();
        using var inner = unitOfWork.BeginScope();

        (await unitOfWork.CommitAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();
        unitOfWork.CommitAttemptCount.Should().Be(0);

        inner.Dispose();
        (await unitOfWork.CommitAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();
        unitOfWork.CommitAttemptCount.Should().Be(0);

        middle.Dispose();
        (await unitOfWork.CommitAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();
        unitOfWork.CommitCallCount.Should().Be(3);
        unitOfWork.CommitAttemptCount.Should().Be(1);
    }

    [Fact]
    public void BeginScope_NestedScopes_CapturesOwnershipForEachScope()
    {
        var unitOfWork = new FakeUnitOfWork();
        using var owner = unitOfWork.BeginScope();
        var nested = unitOfWork.BeginScope();

        owner.IsOwner.Should().BeTrue();
        nested.IsOwner.Should().BeFalse();

        nested.Dispose();

        owner.IsOwner.Should().BeTrue();
        nested.IsOwner.Should().BeFalse();
        using var nextNested = unitOfWork.BeginScope();
        nextNested.IsOwner.Should().BeFalse();
    }

    [Fact]
    public void Dispose_OwnerScope_DoesNotCommitAndAllowsNewOwner()
    {
        var unitOfWork = new FakeUnitOfWork();
        var scope = unitOfWork.BeginScope();

        scope.Dispose();

        unitOfWork.CommitCallCount.Should().Be(0);
        unitOfWork.CommitAttemptCount.Should().Be(0);
        using var next = unitOfWork.BeginScope();
        next.IsOwner.Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_NestedScope_IsIdempotentAndPreservesOwnerDepth()
    {
        var unitOfWork = new FakeUnitOfWork();
        using var owner = unitOfWork.BeginScope();
        var nested = unitOfWork.BeginScope();

        nested.Dispose();
        nested.Dispose();

        using var nextNested = unitOfWork.BeginScope();
        nextNested.IsOwner.Should().BeFalse();
        (await unitOfWork.CommitAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();
        unitOfWork.CommitAttemptCount.Should().Be(0);

        nextNested.Dispose();
        (await unitOfWork.CommitAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();
        unitOfWork.CommitCallCount.Should().Be(2);
        unitOfWork.CommitAttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_PreviousOwnerAgain_DoesNotChangeNewScopeDepth()
    {
        var unitOfWork = new FakeUnitOfWork();
        var previous = unitOfWork.BeginScope();
        previous.Dispose();
        using var owner = unitOfWork.BeginScope();

        previous.Dispose();

        using var nested = unitOfWork.BeginScope();
        nested.IsOwner.Should().BeFalse();
        (await unitOfWork.CommitAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();
        unitOfWork.CommitAttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task CommitAsync_RepeatedOwnerCalls_CountsEachAttempt()
    {
        var unitOfWork = new FakeUnitOfWork();
        using var scope = unitOfWork.BeginScope();

        (await unitOfWork.CommitAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();
        (await unitOfWork.CommitAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();

        unitOfWork.CommitCallCount.Should().Be(2);
        unitOfWork.CommitAttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task CommitAsync_UpdatedResult_UsesCurrentConfigurationWithoutPermanentRejection()
    {
        var unitOfWork = new FakeUnitOfWork
        {
            CommitResult = Result.Fail(new Error.Conflict("test.commit-failed"))
        };

        (await unitOfWork.CommitAsync(TestContext.Current.CancellationToken)).Should().BeFailure();
        unitOfWork.CommitResult = Result.Ok();

        (await unitOfWork.CommitAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();
        unitOfWork.CommitCallCount.Should().Be(2);
        unitOfWork.CommitAttemptCount.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitAsync_CanceledToken_WithOrWithoutScope_ReturnsCanceledTaskAndCountsAttempt(bool openScope)
    {
        var unitOfWork = new FakeUnitOfWork
        {
            CommitResult = Result.Fail(new Error.Conflict("test.commit-failed"))
        };
        using var scope = openScope ? unitOfWork.BeginScope() : null;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var task = unitOfWork.CommitAsync(cancellation.Token);

        task.IsCanceled.Should().BeTrue();
        Func<Task> act = () => task;
        var assertion = await act.Should().ThrowAsync<OperationCanceledException>();
        assertion.Which.CancellationToken.Should().Be(cancellation.Token);
        unitOfWork.CommitCallCount.Should().Be(1);
        unitOfWork.CommitAttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task CommitAsync_CanceledNestedScope_DefersCancellationUntilOwnerCommit()
    {
        var unitOfWork = new FakeUnitOfWork();
        using var owner = unitOfWork.BeginScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using (unitOfWork.BeginScope())
        {
            (await unitOfWork.CommitAsync(cancellation.Token)).Should().BeSuccess();
            unitOfWork.CommitCallCount.Should().Be(1);
            unitOfWork.CommitAttemptCount.Should().Be(0);
        }

        Func<Task> act = () => unitOfWork.CommitAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        unitOfWork.CommitCallCount.Should().Be(2);
        unitOfWork.CommitAttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task CommitAsync_AfterCanceledAttempt_CanCommitWithUncanceledToken()
    {
        var unitOfWork = new FakeUnitOfWork();
        using var scope = unitOfWork.BeginScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> act = () => unitOfWork.CommitAsync(cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        (await unitOfWork.CommitAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();

        unitOfWork.CommitCallCount.Should().Be(2);
        unitOfWork.CommitAttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task BeginScope_DifferentInstances_KeepOwnershipAndCountersIndependent()
    {
        var first = new FakeUnitOfWork();
        var second = new FakeUnitOfWork();
        using var firstOwner = first.BeginScope();
        using var firstNested = first.BeginScope();
        using var secondOwner = second.BeginScope();

        firstOwner.IsOwner.Should().BeTrue();
        firstNested.IsOwner.Should().BeFalse();
        secondOwner.IsOwner.Should().BeTrue();
        (await first.CommitAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();
        (await second.CommitAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();
        first.CommitCallCount.Should().Be(1);
        first.CommitAttemptCount.Should().Be(0);
        second.CommitCallCount.Should().Be(1);
        second.CommitAttemptCount.Should().Be(1);
    }

    [Fact]
    public void BeginScope_AfterExceptionalExit_RestoresOwnershipWithoutCommit()
    {
        var unitOfWork = new FakeUnitOfWork();
        Action act = () =>
        {
            using var scope = unitOfWork.BeginScope();
            throw new InvalidOperationException("test failure");
        };

        act.Should().Throw<InvalidOperationException>();

        using var next = unitOfWork.BeginScope();
        next.IsOwner.Should().BeTrue();
        unitOfWork.CommitCallCount.Should().Be(0);
        unitOfWork.CommitAttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task CommitAsync_InterfaceContract_SupportsScopeAndCommit()
    {
        var unitOfWork = new FakeUnitOfWork();
        unitOfWork.Should().BeAssignableTo<IUnitOfWork>();
        using var scope = unitOfWork.BeginScope();

        scope.IsOwner.Should().BeTrue();
        (await unitOfWork.CommitAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();
    }
}