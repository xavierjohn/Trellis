namespace Trellis.Showcase.Tests.Domain;

using Trellis.Showcase.Domain.Aggregates;

/// <summary>
/// Pins down the per-value behavior carried by the <see cref="RequiredEnum{TSelf}"/> conversion of
/// <see cref="AccountStatus"/> and <see cref="AccountType"/>. These properties replace string/enum
/// duplication elsewhere in the codebase (e.g., <c>BankAccount.PayInterest</c> uses
/// <see cref="AccountType.EarnsInterest"/>).
/// </summary>
public class AccountEnumBehaviorTests
{
    [Fact]
    public void AccountStatus_IsTerminal_only_true_for_Closed()
    {
        AccountStatus.Active.IsTerminal.Should().BeFalse();
        AccountStatus.Frozen.IsTerminal.Should().BeFalse();
        AccountStatus.Closed.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void AccountType_EarnsInterest_true_for_savings_products_only()
    {
        AccountType.Checking.EarnsInterest.Should().BeFalse();
        AccountType.Savings.EarnsInterest.Should().BeTrue();
        AccountType.MoneyMarket.EarnsInterest.Should().BeTrue();
    }

    [Fact]
    public void AccountStatus_GetAll_returns_three_values_in_declaration_order() =>
        AccountStatus.GetAll().Should().Equal([AccountStatus.Active, AccountStatus.Frozen, AccountStatus.Closed]);

    [Fact]
    public void AccountType_GetAll_returns_three_values_in_declaration_order() =>
        AccountType.GetAll().Should().Equal([AccountType.Checking, AccountType.Savings, AccountType.MoneyMarket]);

    [Fact]
    public void AccountStatus_serializes_value_as_field_name()
    {
        AccountStatus.Active.Value.Should().Be("Active");
        AccountStatus.Frozen.Value.Should().Be("Frozen");
        AccountStatus.Closed.Value.Should().Be("Closed");
    }
}
