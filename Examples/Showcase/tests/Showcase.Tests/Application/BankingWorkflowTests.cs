namespace Trellis.Showcase.Tests.Application;

using System.Collections.Concurrent;
using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Application.Persistence;
using Trellis.Showcase.Application.Services;
using Trellis.Showcase.Application.Workflows;
using Trellis.Showcase.Domain.Aggregates;
using Trellis.Showcase.Domain.ValueObjects;
using Trellis.Testing;

public class BankingWorkflowTests
{
    [Theory]
    [InlineData(1000, true, false, true, "fraud:withdrawal,publish:MoneyWithdrawn")]
    [InlineData(1001, true, true, true, "fraud:withdrawal,identity,publish:MoneyWithdrawn")]
    [InlineData(1001, false, true, false, "fraud:withdrawal")]
    [InlineData(1001, true, false, false, "fraud:withdrawal,identity")]
    public async Task SecureWithdraw_SequentialChecks_PreserveThresholdAndSideEffectOrder(
        int amount, bool fraudPasses, bool identityPasses, bool succeeds, string expectedCalls)
    {
        var token = TestContext.Current.CancellationToken;
        var calls = new List<string>();
        var account = NewAccount();
        var workflow = new BankingWorkflow(new InMemoryAccountRepository(),
            new FraudGateway(calls, fraudPasses, token), new IdentityVerifier(calls, identityPasses, token),
            new EventPublisher(calls, token), TimeProvider.System);

        var result = await workflow.SecureWithdrawAsync(account, Money.Create(amount, "USD"), "123456", token);

        if (succeeds)
            result.Should().BeSuccess().Which.Should().BeSameAs(account);
        else
            result.Should().BeFailure();
        calls.Should().Equal(expectedCalls.Split(','));
        account.Balance.Amount.Should().Be(succeeds ? 5000m - amount : 5000m);
        account.Transactions.Should().HaveCount(succeeds ? 1 : 0);
        account.UncommittedEvents().Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Transfer_IndependentFraudChecks_PreserveAccumulationAndPublicationOrder(bool fraudPasses)
    {
        var token = TestContext.Current.CancellationToken;
        var calls = new List<string>();
        var from = NewAccount();
        var to = NewAccount();
        var workflow = new BankingWorkflow(new InMemoryAccountRepository(),
            new FraudGateway(calls, fraudPasses, token), new IdentityVerifier(calls, true, token),
            new EventPublisher(calls, token), TimeProvider.System);

        var result = await workflow.TransferAsync(from, to, Money.Create(100m, "USD"), "transfer", token);

        if (fraudPasses)
        {
            result.Should().BeSuccess();
            calls.Should().Equal([
                "fraud:transfer-out", "fraud:transfer-in",
                "publish:MoneyWithdrawn", "publish:TransferCompleted", "publish:MoneyDeposited"]);
        }
        else
        {
            var failure = result.Should().BeFailureOfType<Error.Aggregate>().Which;
            failure.Errors.Items.Select(error => error.Detail)
                .Should().Equal(["Rejected transfer-out", "Rejected transfer-in"]);
            calls.Should().Equal(["fraud:transfer-out", "fraud:transfer-in"]);
        }

        from.Balance.Amount.Should().Be(fraudPasses ? 4900m : 5000m);
        to.Balance.Amount.Should().Be(fraudPasses ? 5100m : 5000m);
        from.UncommittedEvents().Should().BeEmpty();
        to.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task Transfer_PendingFraudChecks_StartsBothBeforeEitherCompletes()
    {
        var token = TestContext.Current.CancellationToken;
        var calls = new List<string>();
        var fraud = new GatedFraudGateway(token);
        var from = NewAccount();
        var to = NewAccount();
        var workflow = new BankingWorkflow(new InMemoryAccountRepository(),
            fraud, new IdentityVerifier(calls, true, token),
            new EventPublisher(calls, token), TimeProvider.System);

        var transfer = workflow.TransferAsync(from, to, Money.Create(100m, "USD"), "transfer", token);
        Result<(BankAccount From, BankAccount To)> result;
        try
        {
            await fraud.BothStarted.WaitAsync(TimeSpan.FromSeconds(10), token);

            fraud.Calls.Should().Equal(["transfer-out", "transfer-in"]);
            transfer.IsCompleted.Should().BeFalse();
            from.Balance.Amount.Should().Be(5000m);
            to.Balance.Amount.Should().Be(5000m);
            calls.Should().BeEmpty();
        }
        finally
        {
            fraud.Release();
            result = await transfer.WaitAsync(token);
        }

        result.Should().BeSuccess();
        from.Balance.Amount.Should().Be(4900m);
        to.Balance.Amount.Should().Be(5100m);
        calls.Should().Equal(["publish:MoneyWithdrawn", "publish:TransferCompleted", "publish:MoneyDeposited"]);
    }

    [Fact]
    public async Task SecureWithdraw_CancelledFraudCheck_PropagatesWithoutMutation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var calls = new List<string>();
        var account = NewAccount();
        var workflow = new BankingWorkflow(new InMemoryAccountRepository(),
            new FraudGateway(calls, true, cancellation.Token),
            new IdentityVerifier(calls, true, cancellation.Token),
            new EventPublisher(calls, cancellation.Token), TimeProvider.System);

        Func<Task> withdraw = () => workflow.SecureWithdrawAsync(
            account, Money.Create(1001m, "USD"), "123456", cancellation.Token);

        await withdraw.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().BeEmpty();
        account.Balance.Amount.Should().Be(5000m);
        account.Transactions.Should().BeEmpty();
        account.UncommittedEvents().Should().BeEmpty();
    }

    private static BankAccount NewAccount() =>
        BankAccount.Hydrate(AccountId.NewUniqueV4(), CustomerId.NewUniqueV4(), AccountType.Checking,
            Money.Create(5000m, "USD"), Money.Create(5000m, "USD"), Money.Create(0m, "USD"), AccountStatus.Active);

    private sealed class FraudGateway(List<string> calls, bool passes, CancellationToken expectedToken) : IFraudGateway
    {
        public Task<Result<Unit>> AnalyzeTransactionAsync(
            BankAccount account, Money amount, string transactionType, CancellationToken cancellationToken = default)
        {
            cancellationToken.Should().Be(expectedToken);
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add($"fraud:{transactionType}");
            return Result.Ensure(passes,
                () => new Error.Conflict(null, "fraud.detected") { Detail = $"Rejected {transactionType}" }).AsTask();
        }
    }

    private sealed class IdentityVerifier(List<string> calls, bool passes, CancellationToken expectedToken) : IIdentityVerifier
    {
        public Task<Result<Unit>> VerifyAsync(
            CustomerId customerId, string verificationCode, CancellationToken cancellationToken = default)
        {
            cancellationToken.Should().Be(expectedToken);
            calls.Add("identity");
            return Result.Ensure(passes, () => new Error.AuthenticationRequired()).AsTask();
        }
    }

    private sealed class GatedFraudGateway(CancellationToken expectedToken) : IFraudGateway
    {
        private readonly TaskCompletionSource _bothStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<Result<Unit>> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<string> Calls { get; } = new();
        public Task BothStarted => _bothStarted.Task;

        public Task<Result<Unit>> AnalyzeTransactionAsync(
            BankAccount account, Money amount, string transactionType, CancellationToken cancellationToken = default)
        {
            cancellationToken.Should().Be(expectedToken);
            Calls.Enqueue(transactionType);
            if (Calls.Count == 2)
                _bothStarted.TrySetResult();

            return _release.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _release.SetResult(Result.Ok());
    }

    private sealed class EventPublisher(List<string> calls, CancellationToken expectedToken) : IEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            cancellationToken.Should().Be(expectedToken);
            calls.Add($"publish:{domainEvent.GetType().Name}");
            return Task.CompletedTask;
        }
    }
}
