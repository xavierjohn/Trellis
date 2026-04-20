namespace Trellis.Showcase.Application;

using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Application.Persistence;
using Trellis.Showcase.Domain.Aggregates;
using Trellis.Showcase.Domain.ValueObjects;

/// <summary>
/// Seeds three demo accounts. The hardcoded GUIDs are guaranteed-valid input to
/// <see cref="RequiredGuid{T}.TryCreate(Guid)"/>; <see cref="Required"/> centralizes
/// the seed-time invariant check so production code never reaches for <c>.Value</c>.
/// </summary>
public static class ShowcaseSeed
{
    public static readonly CustomerId AliceId = Required(CustomerId.TryCreate(new Guid("11111111-1111-1111-1111-111111111111")));
    public static readonly CustomerId BobId = Required(CustomerId.TryCreate(new Guid("22222222-2222-2222-2222-222222222222")));

    public static readonly AccountId AliceCheckingId = Required(AccountId.TryCreate(new Guid("aaaaaaa1-0000-0000-0000-000000000000")));
    public static readonly AccountId AliceSavingsId = Required(AccountId.TryCreate(new Guid("aaaaaaa2-0000-0000-0000-000000000000")));
    public static readonly AccountId BobCheckingId = Required(AccountId.TryCreate(new Guid("bbbbbbb1-0000-0000-0000-000000000000")));

    public static void Apply(IAccountRepository repository, TimeProvider timeProvider)
    {
        var aliceChecking = BankAccount.Hydrate(AliceCheckingId, AliceId, AccountType.Checking, Money.Create(1000m, "USD"), Money.Create(500m, "USD"), Money.Create(0m, "USD"), AccountStatus.Active, timeProvider);
        var aliceSavings = BankAccount.Hydrate(AliceSavingsId, AliceId, AccountType.Savings, Money.Create(5000m, "USD"), Money.Create(1000m, "USD"), Money.Create(0m, "USD"), AccountStatus.Active, timeProvider);
        var bobChecking = BankAccount.Hydrate(BobCheckingId, BobId, AccountType.Checking, Money.Create(250m, "USD"), Money.Create(500m, "USD"), Money.Create(0m, "USD"), AccountStatus.Active, timeProvider);

        repository.Add(aliceChecking);
        repository.Add(aliceSavings);
        repository.Add(bobChecking);
    }

    private static T Required<T>(Result<T> result) =>
        result.Match(
            onSuccess: ok => ok,
            onFailure: err => throw new InvalidOperationException($"Showcase seed value invalid: {err.GetDisplayMessage()}"));
}
