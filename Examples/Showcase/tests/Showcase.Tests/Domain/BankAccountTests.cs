namespace Trellis.Showcase.Tests.Domain;

using Microsoft.Extensions.Time.Testing;
using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Domain.Aggregates;
using Trellis.Showcase.Domain.Events;
using Trellis.Showcase.Domain.ValueObjects;
using Trellis.Testing;

public class BankAccountTests
{
    private static readonly DateTime FixedNow = new(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc);

    private static BankAccount NewActiveAccount(decimal initialDeposit = 100m, decimal dailyLimit = 500m, decimal overdraft = 0m, AccountType? type = null)
    {
        var timeProvider = new FakeTimeProvider(FixedNow);
        var result = BankAccount.TryCreate(
            CustomerId.NewUniqueV4(),
            type ?? AccountType.Checking,
            Money.Create(initialDeposit, "USD"),
            Money.Create(dailyLimit, "USD"),
            Money.Create(overdraft, "USD"),
            timeProvider);
        result.IsSuccess.Should().BeTrue();
        return result.Unwrap();
    }

    [Fact]
    public void TryCreate_with_zero_daily_limit_returns_unprocessable_content()
    {
        var result = BankAccount.TryCreate(
            CustomerId.NewUniqueV4(),
            AccountType.Checking,
            Money.Create(100m, "USD"),
            Money.Create(0m, "USD"),
            Money.Create(0m, "USD"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<Error.UnprocessableContent>();
        var fields = ((Error.UnprocessableContent)result.Error!).Fields;
        fields.Length.Should().Be(1);
    }

    [Fact]
    public void TryCreate_emits_AccountOpened_event()
    {
        var account = NewActiveAccount();

        var events = account.UncommittedEvents();
        events.Should().HaveCount(1);
        events[0].Should().BeOfType<AccountOpened>();
    }

    [Fact]
    public void Deposit_increases_balance_and_emits_event()
    {
        var account = NewActiveAccount(100m);

        var result = account.Deposit(Money.Create(50m, "USD"));

        result.IsSuccess.Should().BeTrue();
        account.Balance.Amount.Should().Be(150m);
        account.UncommittedEvents().OfType<MoneyDeposited>().Should().HaveCount(1);
    }

    [Fact]
    public void Deposit_to_frozen_account_returns_conflict()
    {
        var account = NewActiveAccount();
        account.Freeze("test").IsSuccess.Should().BeTrue();

        var result = account.Deposit(Money.Create(50m, "USD"));

        result.IsFailure.Should().BeTrue();
        var conflict = result.Error.Should().BeOfType<Error.Conflict>().Subject;
        conflict.ReasonCode.Should().Be("account.not.active");
    }

    [Fact]
    public void Withdraw_exceeding_daily_limit_returns_conflict()
    {
        var account = NewActiveAccount(initialDeposit: 1000m, dailyLimit: 100m);

        var result = account.Withdraw(Money.Create(150m, "USD"));

        result.IsFailure.Should().BeTrue();
        var conflict = result.Error.Should().BeOfType<Error.Conflict>().Subject;
        conflict.ReasonCode.Should().Be("withdrawal.daily.limit");
    }

    [Fact]
    public void Lifecycle_invalid_transition_via_state_machine_returns_conflict()
    {
        var account = NewActiveAccount();

        // Active accounts cannot be unfrozen — Stateless invalid-transition flows through FireResult.
        var result = account.Unfreeze();

        result.IsFailure.Should().BeTrue();
        var unproc = result.Error.Should().BeOfType<Error.UnprocessableContent>().Subject;
        unproc.Rules.Items.Should().ContainSingle().Which.ReasonCode.Should().Be("state.machine.invalid.transition");
    }

    [Fact]
    public void Close_with_nonzero_balance_returns_conflict_before_state_machine_runs()
    {
        var account = NewActiveAccount(100m);

        var result = account.Close();

        result.IsFailure.Should().BeTrue();
        var conflict = result.Error.Should().BeOfType<Error.Conflict>().Subject;
        conflict.ReasonCode.Should().Be("account.close.nonzero.balance");
        account.Status.Should().Be(AccountStatus.Active);
    }

    [Fact]
    public void Freeze_then_Unfreeze_returns_account_to_active()
    {
        var account = NewActiveAccount();

        account.Freeze("audit").IsSuccess.Should().BeTrue();
        account.Status.Should().Be(AccountStatus.Frozen);

        account.Unfreeze().IsSuccess.Should().BeTrue();
        account.Status.Should().Be(AccountStatus.Active);
    }

    [Fact]
    public void TransferTo_self_returns_conflict()
    {
        var account = NewActiveAccount(100m);

        var result = account.TransferTo(account, Money.Create(10m, "USD"));

        result.IsFailure.Should().BeTrue();
        var conflict = result.Error.Should().BeOfType<Error.Conflict>().Subject;
        conflict.ReasonCode.Should().Be("transfer.same.account");
    }

    [Fact]
    public void Closed_account_freeze_returns_conflict_and_emits_no_events()
    {
        var account = NewActiveAccount(0m); // zero balance to allow Close
        account.Close().IsSuccess.Should().BeTrue();
        account.Status.Should().Be(AccountStatus.Closed);
        account.AcceptChanges();

        var result = account.Freeze("audit");

        result.IsFailure.Should().BeTrue();
        var unproc = result.Error.Should().BeOfType<Error.UnprocessableContent>().Subject;
        unproc.Rules.Items.Should().ContainSingle().Which.ReasonCode.Should().Be("state.machine.invalid.transition");
        account.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public void Closed_account_unfreeze_returns_conflict_and_emits_no_events()
    {
        var account = NewActiveAccount(0m);
        account.Close().IsSuccess.Should().BeTrue();
        account.AcceptChanges();

        var result = account.Unfreeze();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<Error.UnprocessableContent>();
        account.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public void Closed_account_close_again_returns_conflict_and_emits_no_events()
    {
        var account = NewActiveAccount(0m);
        account.Close().IsSuccess.Should().BeTrue();
        account.AcceptChanges();

        var result = account.Close();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<Error.UnprocessableContent>();
        account.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public void TransferTo_frozen_destination_does_not_mutate_source()
    {
        var from = NewActiveAccount(100m, dailyLimit: 1000m);
        var to = NewActiveAccount(0m);
        to.Freeze("audit").IsSuccess.Should().BeTrue();
        from.AcceptChanges();
        to.AcceptChanges();

        var result = from.TransferTo(to, Money.Create(40m, "USD"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<Error.Conflict>();
        from.Balance.Amount.Should().Be(100m);
        to.Balance.Amount.Should().Be(0m);
        from.Transactions.Should().BeEmpty();
        to.Transactions.Should().BeEmpty();
        from.UncommittedEvents().Should().BeEmpty();
        to.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public void TransferTo_other_moves_money_and_emits_TransferCompleted()
    {
        var from = NewActiveAccount(100m, dailyLimit: 1000m);
        from.AcceptChanges();
        var to = NewActiveAccount(0m);
        to.AcceptChanges();

        var result = from.TransferTo(to, Money.Create(40m, "USD"));

        result.IsSuccess.Should().BeTrue();
        from.Balance.Amount.Should().Be(60m);
        to.Balance.Amount.Should().Be(40m);
        from.UncommittedEvents().OfType<TransferCompleted>().Should().HaveCount(1);
    }
}