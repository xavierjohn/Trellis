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

    /// <summary>
    /// Returns at most <paramref name="limit"/> accounts after <paramref name="cursor"/> (or
    /// from the start when <paramref name="cursor"/> is <c>null</c>), ordered by
    /// <see cref="AccountId"/> ascending. The server applies a hard cap of 5 regardless of
    /// the requested limit. A malformed cursor produces <see cref="Error.InvalidInput"/>
    /// carrying a field violation on <c>cursor</c>.
    /// </summary>
    Result<Page<BankAccount>> GetPage(int limit, Cursor? cursor);
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
                : Result.Fail<BankAccount>(new Error.NotFound(ResourceRef.For<BankAccount>(id))
                {
                    Code = "account.not-found",
                    Detail = $"No account exists with id '{id.Value}'.",
                });
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

    private const int ServerCap = 5;

    public Result<Page<BankAccount>> GetPage(int limit, Cursor? cursor)
    {
        var pageSize = PageSize.FromRequested(limit <= 0 ? 10 : limit, max: ServerCap);

        Guid? afterId = null;
        if (cursor is { } c)
        {
            var decoded = CursorCodec.TryDecode<Guid>(c);
            if (decoded.IsFailure)
                return Result.Fail<Page<BankAccount>>(decoded.Error!);

            decoded.TryGetValue(out var id);
            afterId = id;
        }

        lock (_gate)
        {
            var overFetched = _accounts.Values
                .OrderBy(a => a.Id.Value)
                .Where(a => afterId is not Guid g || a.Id.Value.CompareTo(g) > 0)
                .Take(pageSize.Applied + 1)
                .ToList();

            return Result.Ok(PageBuilder.FromOverFetch(overFetched, pageSize, a => a.Id.Value));
        }
    }
}