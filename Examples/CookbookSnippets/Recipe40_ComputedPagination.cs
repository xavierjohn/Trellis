namespace CookbookSnippets.Recipe40;

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