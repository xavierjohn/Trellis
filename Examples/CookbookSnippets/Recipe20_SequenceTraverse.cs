// Cookbook Recipe 20 — Fail-fast vs accumulating: Sequence/Traverse vs SequenceAll/TraverseAll.
namespace CookbookSnippets.Recipe20;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Primitives;

public sealed record CreateContactRow(string Email, string Name);

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed class Order : Aggregate<OrderId>
{
    private Order(OrderId id) : base(id) { }

    public static Order ForTesting(OrderId id) => new(id);
}

public interface IOrderRepository
{
    Task<Result<Order>> LoadAsync(OrderId id, CancellationToken ct);
}

public sealed class Recipe20CompiledSurface(IOrderRepository repo)
{
    public Result<IReadOnlyList<EmailAddress>> ValidateAddresses(IEnumerable<CreateContactRow> rows)
    {
        _ = repo;
        return rows.TraverseAll(row => EmailAddress.TryCreate(row.Email));
    }

    public Task<Result<IReadOnlyList<Order>>> LoadOrders(IEnumerable<OrderId> ids, CancellationToken ct) =>
        ids.TraverseAsync((id, c) =>
        {
            _ = c.CanBeCanceled;
            return repo.LoadAsync(id, c);
        }, ct);
}

internal static class Recipe20Demonstrator
{
    public static Result<IReadOnlyList<EmailAddress>> FailFastTraverse(IEnumerable<CreateContactRow> rows) =>
        rows.Traverse(row => EmailAddress.TryCreate(row.Email));

    public static Result<IReadOnlyList<EmailAddress>> AccumulatingTraverse(IEnumerable<CreateContactRow> rows) =>
        rows.TraverseAll(row => EmailAddress.TryCreate(row.Email));

    public static Result<IReadOnlyList<EmailAddress>> FailFastSequence(IEnumerable<Result<EmailAddress>> results) =>
        results.Sequence();

    public static Result<IReadOnlyList<EmailAddress>> AccumulatingSequence(IEnumerable<Result<EmailAddress>> results) =>
        results.SequenceAll();

    public static Task<Result<IReadOnlyList<EmailAddress>>> TaskTraverse(IEnumerable<CreateContactRow> rows) =>
        rows.TraverseAsync(row => Task.FromResult(EmailAddress.TryCreate(row.Email)));

    public static Task<Result<IReadOnlyList<EmailAddress>>> TaskTraverseWithCancellation(IEnumerable<CreateContactRow> rows, CancellationToken ct) =>
        rows.TraverseAsync((row, token) => Task.FromResult(EmailAddress.TryCreate(row.Email, nameof(row.Email))), ct);

    public static ValueTask<Result<IReadOnlyList<EmailAddress>>> ValueTaskTraverse(IEnumerable<CreateContactRow> rows) =>
        rows.TraverseAsync(row => new ValueTask<Result<EmailAddress>>(EmailAddress.TryCreate(row.Email)));

    public static ValueTask<Result<IReadOnlyList<EmailAddress>>> ValueTaskTraverseWithCancellation(IEnumerable<CreateContactRow> rows, CancellationToken ct) =>
        rows.TraverseAsync((row, token) => new ValueTask<Result<EmailAddress>>(EmailAddress.TryCreate(row.Email, nameof(row.Email))), ct);

    public static Task<Result<Unit>> UnitTraverseWithCancellation(IEnumerable<CreateContactRow> rows, CancellationToken ct) =>
        rows.TraverseAsync((row, token) => Task.FromResult(EmailAddress.TryCreate(row.Email).Map(_ => Unit.Default)), ct);

    public static Task<Result<IReadOnlyList<EmailAddress>>> TaskTraverseAll(IEnumerable<CreateContactRow> rows) =>
        rows.TraverseAllAsync(row => Task.FromResult(EmailAddress.TryCreate(row.Email)));

    public static Task<Result<IReadOnlyList<EmailAddress>>> TaskTraverseAllWithCancellation(IEnumerable<CreateContactRow> rows, CancellationToken ct) =>
        rows.TraverseAllAsync((row, token) => Task.FromResult(EmailAddress.TryCreate(row.Email, nameof(row.Email))), ct);

    public static ValueTask<Result<IReadOnlyList<EmailAddress>>> ValueTaskTraverseAll(IEnumerable<CreateContactRow> rows) =>
        rows.TraverseAllAsync(row => new ValueTask<Result<EmailAddress>>(EmailAddress.TryCreate(row.Email)));

    public static ValueTask<Result<IReadOnlyList<EmailAddress>>> ValueTaskTraverseAllWithCancellation(IEnumerable<CreateContactRow> rows, CancellationToken ct) =>
        rows.TraverseAllAsync((row, token) => new ValueTask<Result<EmailAddress>>(EmailAddress.TryCreate(row.Email, nameof(row.Email))), ct);

    public static Task<Result<Unit>> UnitTraverseAllWithCancellation(IEnumerable<CreateContactRow> rows, CancellationToken ct) =>
        rows.TraverseAllAsync((row, token) => Task.FromResult(EmailAddress.TryCreate(row.Email).Map(_ => Unit.Default)), ct);

    public static Error CombineErrors(Error first, Error second)
    {
        Error? accumulated = null;
        accumulated = accumulated.Combine(first);
        return accumulated.Combine(second);
    }

    public static Result<EmailAddress> EnsureAllPin(Result<EmailAddress> seed) =>
        seed.EnsureAll(
            (email => email.Value.Length > 0, Error.InvalidInput.ForField("email", ValidationCodes.ValueNotEmpty)),
            (email => email.Value.Contains('@'), Error.InvalidInput.ForField("email", ValidationCodes.StringEmail)));
}

#if FALSE
// Wrong — manual loop with early return loses every error after the first and often reaches for test-only Unwrap().
#endif