namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;

/// <summary>
/// Result of evaluating a Range request per RFC 9110 §14.
/// </summary>
public abstract record RangeOutcome
{
    private RangeOutcome() { }

    /// <summary>No Range header or range not applicable — serve full representation.</summary>
    public sealed record FullRepresentation : RangeOutcome;

    /// <summary>Range satisfiable — serve partial content (206).</summary>
    public sealed record PartialContent(long From, long To, long CompleteLength) : RangeOutcome;

    /// <summary>Range not satisfiable (416).</summary>
    public sealed record NotSatisfiable(long CompleteLength) : RangeOutcome;
}

/// <summary>
/// Evaluates RFC 9110 §14 Range request headers.
/// Only supports byte ranges. Non-GET requests are treated as Full (Range ignored).
/// </summary>
public static class RangeRequestEvaluator
{
    /// <summary>
    /// Evaluates the Range header against the representation's complete length.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="completeLength">The total size of the representation in bytes.</param>
    /// <returns>A <see cref="RangeOutcome"/> indicating how to respond.</returns>
    public static RangeOutcome Evaluate(HttpRequest request, long completeLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completeLength);

        // RFC 9110 §14.2: Range is only applicable to GET
        if (!HttpMethods.IsGet(request.Method))
            return new RangeOutcome.FullRepresentation();

        var rangeHeader = request.GetTypedHeaders().Range;
        if (rangeHeader is null)
            return new RangeOutcome.FullRepresentation();

        // Only support bytes unit
        if (!string.Equals(rangeHeader.Unit.ToString(), "bytes", StringComparison.OrdinalIgnoreCase))
            return new RangeOutcome.FullRepresentation();

        if (rangeHeader.Ranges is not { Count: > 0 })
            return new RangeOutcome.FullRepresentation();

        // Multi-range requests require multipart/byteranges (not supported) — ignore Range header
        if (rangeHeader.Ranges.Count > 1)
            return new RangeOutcome.FullRepresentation();

        var range = rangeHeader.Ranges.First();

        long from, to;

        if (range.From.HasValue && range.To.HasValue)
        {
            from = range.From.Value;
            to = Math.Min(range.To.Value, completeLength - 1);
        }
        else if (range.From.HasValue)
        {
            from = range.From.Value;
            to = completeLength - 1;
        }
        else if (range.To.HasValue)
        {
            // Suffix range: last N bytes
            from = Math.Max(0, completeLength - range.To.Value);
            to = completeLength - 1;
        }
        else
        {
            return new RangeOutcome.FullRepresentation();
        }

        // Satisfiability check
        if (from >= completeLength || from > to)
            return new RangeOutcome.NotSatisfiable(completeLength);

        return new RangeOutcome.PartialContent(from, to, completeLength);
    }
}
