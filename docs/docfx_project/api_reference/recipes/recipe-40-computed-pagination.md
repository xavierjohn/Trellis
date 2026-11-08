---
name: recipe-40-computed-pagination
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows computed pagination with validated, query-bound continuation state. Use when paginating a spherical distance or score rather than a stored column."
---
# Recipe 40 — Computed pagination with validated query-bound continuation state

**Requires:** `Trellis.Core`, plus `Trellis.EntityFrameworkCore`, `Trellis.Primitives`.

## Contents

- [Translated EF Core spherical-distance path](#translated-ef-core-spherical-distance-path)
- [Bounded application-computed fallback](#bounded-application-computed-fallback)

**Problem.** Page a computed distance (or score), preserve a deterministic tie-breaker,
reject impossible boundaries, and reject tokens from a different
origin/filter/algorithm context. Spherical latitude/longitude queries have a translated EF
Core path; other computations remain application-owned and must start from a complete,
bounded candidate set.

## Translated EF Core spherical-distance path

`GeoBounds` validates the radius and derives one or two conservative, non-wrapping boxes.
`GeoCoordinateExpressions.WithinRadius` inlines that broad prefilter and the exact
haversine predicate into one expression. `DistanceMetersTo` supplies the same translated
calculation as the first seek key; the database therefore owns filtering, ordering,
boundary projection, and seeking.

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Trellis;
using Trellis.EntityFrameworkCore;
using Trellis.Primitives;

public static class NearbyVenuePagination
{
    public static Task<Result<Page<Venue>>> ListAsync(
        AppDbContext db,
        GeoCoordinate origin,
        double radiusMeters,
        string scopeFilterIdentity,
        string? cursor,
        int? limit,
        CancellationToken ct)
    {
        return GeoBounds.TryCreate(origin, radiusMeters, nameof(radiusMeters))
            .Combine(PageRequest.TryCreate(cursor, limit))
            .BindAsync(input =>
            {
                var (bounds, request) = input;
                var withinRadius = GeoCoordinateExpressions.WithinRadius<Venue>(
                    venue => venue.Latitude,
                    venue => venue.Longitude,
                    bounds);
                var distance = GeoCoordinateExpressions.DistanceMetersTo<Venue>(
                    venue => venue.Latitude,
                    venue => venue.Longitude,
                    bounds.Center);

                var context = ContextIdentity(bounds, scopeFilterIdentity);
                var seek = SeekDefinition.Ascending(distance)
                    .ThenAscending(venue => venue.Id)
                    .WithCodec(CreateCodec(context, bounds.RadiusMeters));

                return db.Venues
                    .AsNoTracking()
                    .Where(venue => venue.IsPublished)
                    .Where(withinRadius)
                    .ToPageAsync(request, seek, cancellationToken: ct);
            });
    }

    private static ICursorCodec<(double Primary, Guid Secondary)> CreateCodec(
        string context,
        double radiusMeters) =>
        CursorCodec.Map<
            ((double Primary, Guid Secondary) Primary, string Secondary),
            (double Primary, Guid Secondary)>(
            CursorCodec.Composite(
                CursorCodec.Composite<double, Guid>(),
                CursorCodec.Scalar<string>()),
            state => (state, context),
            (wire, field) =>
                double.IsFinite(wire.Primary.Primary)
                && wire.Primary.Primary is >= 0
                && wire.Primary.Primary <= radiusMeters
                && string.Equals(wire.Secondary, context, StringComparison.Ordinal)
                    ? Result.Ok(wire.Primary)
                    : Result.Fail<(double Primary, Guid Secondary)>(
                        Error.InvalidInput.ForField(
                            field: field ?? "cursor",
                            code: "cursor.malformed",
                            detail: "Cursor distance or query context is invalid.")));

    private static string ContextIdentity(GeoBounds bounds, string scopeFilterIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeFilterIdentity);
        var canonical = string.Concat(
            "geo-haversine-v1;distance-asc;guid-asc;",
            scopeFilterIdentity.Length.ToString(CultureInfo.InvariantCulture), ":",
            scopeFilterIdentity, ";",
            Canonical(bounds.Center.Latitude), ";",
            Canonical(bounds.Center.Longitude), ";",
            Canonical(bounds.RadiusMeters));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Canonical(double value) =>
        (value == 0 ? 0d : value).ToString("R", CultureInfo.InvariantCulture);
}
```

`scopeFilterIdentity` is a server-derived identity for the exact authorization and filter
scope; it is not client input and does not replace the `.Where(...)` predicates. Include
every membership/ordering input in the canonical identity. The codec adds that identity to
the wire state and validates it on decode; `WithCodec` changes only continuation encoding,
not filtering. The hash is not a signature — wrap the codec with application-owned
protection when cursor tamper resistance is required.

The broad boxes deliberately admit false positives, while the exact predicate removes
them. They use ordinary numeric columns rather than a provider spatial index. SQLite and
SQL Server are exercised by Trellis integration tests; verify all translated functions and
seek comparisons on other providers. Translation/connection failures propagate without a
client-side fallback. Search values are parameters, so different origins/radii within the
same one-box or two-box structural case reuse the query shape. Provider transcendental
functions can round differently from the in-memory `SinPi`/`CosPi` path at an exact
boundary; include an application tolerance in the validated radius when required.

## Bounded application-computed fallback

This example uses planar Euclidean distance over a **complete, bounded, authorized snapshot** (at most 10,000 candidates with unique IDs). `scopeSnapshotId` is a server-assigned identity for that exact candidate snapshot **and** authorization/filter scope, never a client-supplied substitute for filtering. Production code must obtain that set through an appropriate index/search provider or bounded domain operation; do not load an unbounded table. Coordinates are finite and within ±1,000,000 in the application's units.

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Trellis;

public sealed record DistanceCandidate(Guid Id, double X, double Y);
public sealed record DistanceItem(Guid Id, double Distance);
public sealed record DistanceBoundary(double Distance, Guid Id, string Context);

public static class DistancePagination
{
    public static Result<Page<DistanceItem>> List(
        IReadOnlyList<DistanceCandidate> authorizedSnapshot,
        string scopeSnapshotId, double originX, double originY,
        string? cursor, int? limit)
    {
        if (!ValidCoordinate(originX) || !ValidCoordinate(originY))
            return Result.Fail<Page<DistanceItem>>(Error.InvalidInput.ForField(
                field: "origin", code: "search.origin.invalid", detail: "Origin is outside the supported coordinate range."));

        var context = ContextIdentity(scopeSnapshotId, originX, originY);
        var codec = CreateCodec(context);
        return PageRequest.TryCreate(cursor, limit)
            .BindZip(request => request.Decode(codec))
            .Map((request, boundary) => BuildPage(authorizedSnapshot, originX, originY,
                request.Size, boundary, context, codec));
    }

    private static ICursorCodec<DistanceBoundary> CreateCodec(string context) =>
        CursorCodec.Map<((double Primary, Guid Secondary) Primary, string Secondary), DistanceBoundary>(
            CursorCodec.Composite(
                CursorCodec.Composite<double, Guid>(), CursorCodec.Scalar<string>()),
            state => ((state.Distance, state.Id), state.Context),
            (wire, field) =>
                double.IsFinite(wire.Primary.Primary) && wire.Primary.Primary >= 0
                && string.Equals(wire.Secondary, context, StringComparison.Ordinal)
                    ? Result.Ok(new DistanceBoundary(
                        wire.Primary.Primary, wire.Primary.Secondary, wire.Secondary))
                    : Result.Fail<DistanceBoundary>(Error.InvalidInput.ForField(
                        field: field ?? "cursor", code: "cursor.malformed",
                        detail: "Cursor distance or query context is invalid.")));

    private static Page<DistanceItem> BuildPage(
        IReadOnlyList<DistanceCandidate> candidates, double x, double y,
        PageSize size, Maybe<DistanceBoundary> boundary, string context,
        ICursorCodec<DistanceBoundary> codec)
    {
        if (candidates.Count > 10_000
            || candidates.Select(c => c.Id).Distinct().Count() != candidates.Count
            || candidates.Any(c => !ValidCoordinate(c.X) || !ValidCoordinate(c.Y)))
            throw new ArgumentException("The trusted candidate snapshot violates its bounds.", nameof(candidates));

        IEnumerable<DistanceItem> scored = candidates.Select(c =>
            new DistanceItem(c.Id, Math.Sqrt(
                ((c.X - x) * (c.X - x)) + ((c.Y - y) * (c.Y - y)))));

        if (boundary.TryGetValue(out var after))
            scored = scored.Where(item =>
                item.Distance > after.Distance
                || (item.Distance == after.Distance && item.Id.CompareTo(after.Id) > 0));

        var rows = scored.OrderBy(item => item.Distance).ThenBy(item => item.Id)
            .Take(size.Applied + 1).ToArray();

        return PageBuilder.FromOverFetch(rows, size,
            last => codec.Encode(new DistanceBoundary(last.Distance, last.Id, context)));
    }

    private static bool ValidCoordinate(double value) =>
        double.IsFinite(value) && value is >= -1_000_000 and <= 1_000_000;

    private static string ContextIdentity(string scopeSnapshotId, double x, double y)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeSnapshotId);
        var canonical = string.Concat(
            "distance-v1;asc;guid-asc;",
            scopeSnapshotId.Length.ToString(CultureInfo.InvariantCulture), ":", scopeSnapshotId, ";",
            (x == 0 ? 0d : x).ToString("R", CultureInfo.InvariantCulture), ";",
            (y == 0 ? 0d : y).ToString("R", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
```

**What it shows.**

- `CursorCodec.Composite` safely nests boundary `(distance, id)` with query context. `CursorCodec.Map` gives that wire tuple a named state and validates finite, nonnegative distance and exact context identity. Validation also runs when encoding server boundaries; corrupt state throws rather than generating an unusable next token.
- Context uses invariant round-trip coordinates (normalizing signed zero), a length-prefixed scope/snapshot identity, fixed sort directions, and an algorithm version before hashing. Include every additional filter/parameter that affects membership or ordering in the canonical identity. The hash is **not a signature**: a malicious client can still construct unsigned state. Protection requires a caller-owned codec wrapper, and every request must reapply authorization.
- The boundary filter runs **before** `Take`. Ascending distance ties use ascending `Guid.CompareTo`, matching the in-memory `ThenBy` comparator; GUIDs do not have a C# `>` operator. A descending score needs the corresponding reversed predicate. Never round the boundary for display before encoding it.
- `PageBuilder` does not search, sort, calculate distances, or decode. It calls the encoder once on the last retained row only if over-fetch finds another row. Provider-owned continuation tokens instead belong in a directly constructed `Page<T>`.
- This bounded implementation recomputes and sorts candidates; it is not a constant-time/indexed-search promise. An immutable snapshot identity is an application guarantee here, not a Trellis snapshot feature. Against changing data, even exact cursors cannot promise a frozen result set.

**Anti-pattern → fix.** Raw JSON/base64 with unchecked distance → typed codecs plus validation; global nearest-N candidates before applying a boundary → seek before `Take`; timestamp/score alone → add a stable unique tie-breaker; replaying a token with another origin/filter → validate canonical context; treating cursor signing, spatial indexing, or ellipsoidal distance as built in → explicitly supply the appropriate infrastructure.

**References.** [Primitives geographic bounds](../trellis-api-primitives.md#geobounds), [EF geographic expressions](../trellis-api-efcore.md#geocoordinateexpressions), [Core pagination](../trellis-api-core.md#pagination), [EF seek definitions](../trellis-api-efcore.md#seekdefinition), [Recipe 3](recipe-03-paginated-query.md). `Result` failures propagate through `Bind` / `Map` (TRLS001); `Maybe` is read through guarded `TryGetValue` (TRLS003). No throwing parser is used for expected client-input failures.
