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
    /// the requested limit. A malformed cursor produces <see cref="Error.BadRequest"/>.
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

    private const int ServerCap = 5;

    public Result<Page<BankAccount>> GetPage(int limit, Cursor? cursor)
    {
        var requestedLimit = limit <= 0 ? 10 : limit;
        var appliedLimit = Math.Min(requestedLimit, ServerCap);

        Guid? afterId = null;
        if (cursor is { } c)
        {
            if (!TryDecodeCursor(c, out var decoded))
            {
                return Result.Fail<Page<BankAccount>>(
                    new Error.BadRequest("invalid_cursor") { Detail = "Cursor is not a recognized token." });
            }

            afterId = decoded;
        }

        lock (_gate)
        {
            var ordered = _accounts.Values
                .OrderBy(a => a.Id.Value)
                .Where(a => afterId is not Guid g || a.Id.Value.CompareTo(g) > 0)
                .Take(appliedLimit + 1)
                .ToList();

            var hasMore = ordered.Count > appliedLimit;
            var items = hasMore ? ordered.Take(appliedLimit).ToList() : ordered;
            Cursor? next = hasMore && items.Count > 0
                ? new Cursor(EncodeCursor(items[^1].Id.Value))
                : null;

            return Result.Ok(new Page<BankAccount>(
                Items: items,
                Next: next,
                Previous: null,
                RequestedLimit: requestedLimit,
                AppliedLimit: appliedLimit));
        }
    }

    private static string EncodeCursor(Guid afterId)
    {
        var json = $"{{\"afterId\":\"{afterId}\"}}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Base64UrlEncode(bytes);
    }

    private static bool TryDecodeCursor(Cursor cursor, out Guid afterId)
    {
        afterId = default;
        if (string.IsNullOrEmpty(cursor.Token))
            return false;
        try
        {
            var bytes = Base64UrlDecode(cursor.Token);
            using var doc = System.Text.Json.JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("afterId", out var prop)) return false;
            return Guid.TryParse(prop.GetString(), out afterId);
        }
        catch (System.Text.Json.JsonException) { return false; }
        catch (FormatException) { return false; }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string token)
    {
        var s = token.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }

        return Convert.FromBase64String(s);
    }
}