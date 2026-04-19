namespace Trellis.Showcase.Api;

using Trellis.Primitives;
using Trellis.Showcase.Api.Persistence;
using Trellis.Showcase.Api.Services;
using Trellis.Showcase.Domain.Aggregates;
using Trellis.Showcase.Domain.ValueObjects;

public static class ShowcaseSeed
{
    public static readonly CustomerId AliceId = CustomerId.TryCreate(new Guid("11111111-1111-1111-1111-111111111111")).Value;
    public static readonly CustomerId BobId = CustomerId.TryCreate(new Guid("22222222-2222-2222-2222-222222222222")).Value;

    public static readonly AccountId AliceCheckingId = AccountId.TryCreate(new Guid("aaaaaaa1-0000-0000-0000-000000000000")).Value;
    public static readonly AccountId AliceSavingsId = AccountId.TryCreate(new Guid("aaaaaaa2-0000-0000-0000-000000000000")).Value;
    public static readonly AccountId BobCheckingId = AccountId.TryCreate(new Guid("bbbbbbb1-0000-0000-0000-000000000000")).Value;

    public static void Apply(IAccountRepository repository, IClock clock)
    {
        var aliceChecking = BankAccount.Hydrate(AliceCheckingId, AliceId, AccountType.Checking, Money.Create(1000m, "USD"), Money.Create(500m, "USD"), Money.Create(0m, "USD"), AccountStatus.Active, () => clock.UtcNow);
        var aliceSavings = BankAccount.Hydrate(AliceSavingsId, AliceId, AccountType.Savings, Money.Create(5000m, "USD"), Money.Create(1000m, "USD"), Money.Create(0m, "USD"), AccountStatus.Active, () => clock.UtcNow);
        var bobChecking = BankAccount.Hydrate(BobCheckingId, BobId, AccountType.Checking, Money.Create(250m, "USD"), Money.Create(500m, "USD"), Money.Create(0m, "USD"), AccountStatus.Active, () => clock.UtcNow);

        repository.Add(aliceChecking);
        repository.Add(aliceSavings);
        repository.Add(bobChecking);
    }
}
