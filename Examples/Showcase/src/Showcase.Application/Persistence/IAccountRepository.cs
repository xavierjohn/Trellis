namespace Trellis.Showcase.Application.Persistence;

using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Domain.Aggregates;
using Trellis.Showcase.Domain.ValueObjects;

public interface IAccountRepository
{
    Result<BankAccount> GetById(AccountId id);
    void Add(BankAccount account);
    IReadOnlyList<BankAccount> All();
}

/// <summary>
/// In-memory repository with synchronized access to its internal storage. Repository operations
/// are protected by a lock, but returned <see cref="BankAccount"/> instances remain mutable live
/// objects and are not safe for concurrent mutation without external synchronization. Showcase
/// deliberately avoids an EF Core mapping for the <see cref="BankAccount"/> aggregate — the
/// Stateless <c>StateMachine</c> field would force a non-trivial materialization story that
/// distracts from the error-handling lessons. EF Core integration is taught by the dedicated
/// <c>EfCoreExample</c> sample and by the template.
/// </summary>
public sealed class InMemoryAccountRepository : IAccountRepository
{
    private readonly Dictionary<AccountId, BankAccount> _accounts = new();
    private readonly object _gate = new();

    public Result<BankAccount> GetById(AccountId id)
    {
        lock (_gate)
        {
            return _accounts.TryGetValue(id, out var account)
                ? Result.Ok(account)
                : Result.Fail<BankAccount>(new Error.NotFound(new ResourceRef("Account", id.Value.ToString())));
        }
    }

    public void Add(BankAccount account)
    {
        lock (_gate)
        {
            _accounts[account.Id] = account;
        }
    }

    public IReadOnlyList<BankAccount> All()
    {
        lock (_gate)
        {
            return _accounts.Values.ToList();
        }
    }
}
