// Cookbook Recipe 11 — Anti-pattern → fix gallery (the analyzers in action).
namespace CookbookSnippets.Recipe11;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Trellis;
using Trellis.Asp;
using CookbookSnippets.Recipe01;
using CookbookSnippets.Recipe08;

public static class AntiPatternFixes
{
#pragma warning disable CA1707 // Diagnostic IDs are deliberately embedded in recipe method names for searchability.
    // ─── TRLS001 — Result return value not handled ─────────────────────────
#if FALSE
    // WRONG — Result<T> dropped on the floor.
    public static void TRLS001_Wrong(Result<OrderId> r) => PlaceOrder(default!);
#endif

    public static void TRLS001_Fix(Result<OrderId> r)
    {
        // FIX — handle the value or assign it.
        var _ = r.Match(_ => 0, e => throw new InvalidOperationException(e.ToString()));
    }

    // ─── TRLS003 — Unsafe Maybe.Value ──────────────────────────────────────
    public static void TRLS003_Fix(Customer customer)
    {
        // FIX 1 — guard.
        if (customer.Email.HasValue)
        {
            var v = customer.Email.Value;
            _ = v;
        }

        // FIX 2 — convert to Result.
        Result<EmailAddress> r = customer.Email.ToResult(
            new Error.NotFound(new ResourceRef("Email", customer.Id.Value.ToString())));
        _ = r;
    }

    // ─── TRLS010 — Throwing in a Result chain ──────────────────────────────
    public static Result<Order> TRLS010_Fix(Result<Order> input) =>
        input.Bind(o => Result.Fail<Order>(new Error.Conflict(
            new ResourceRef("Order", o.Id.Value.ToString()), "invalid_state")));

    // ─── TRLS016 — HasIndex on a Maybe<T> property: see Recipe08 FixPattern ─

    // ─── TRLS017 — Wrong attribute namespace on a value object: Recipe 1 FIX block ─

    // ─── TRLS018 — Unsafe Result<T> deconstruction ─────────────────────────
    public static Microsoft.AspNetCore.Http.IResult TRLS018_Fix(Result<EmailAddress> result)
    {
        var (ok, value, err) = result;
        if (!ok) return err!.ToHttpResponse();
        SendEmail(value!);
        return Results.Ok();
    }

    // ─── TRLS019 — default(Result) / default(Maybe<T>) ─────────────────────
    public static Result TRLS019_FixResult() => Result.Ok();
    public static Maybe<EmailAddress> TRLS019_FixMaybe() => Maybe<EmailAddress>.None;
#pragma warning restore CA1707

    // Helpers used above — minimal stubs.
    private static Result<OrderId> PlaceOrder(object cmd) => Result.Fail<OrderId>(new Error.NotFound(new ResourceRef("Order")));
    private static void SendEmail(EmailAddress _) { }
}
