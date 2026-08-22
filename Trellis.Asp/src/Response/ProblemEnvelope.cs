namespace Trellis.Asp;

using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Trellis;

/// <summary>
/// The two members every Trellis failure response carries at the root of its Problem Details
/// document: <c>code</c> — the machine-readable reason, or the <c>error.unspecified</c> sentinel
/// when the producer named none — and <c>kind</c>, the wire slug for the condition.
/// </summary>
/// <remarks>
/// <para>
/// More than one layer writes a failure. <see cref="ResponseFailureWriter"/> answers for a
/// handler's <see cref="Error"/>; the binder seams answer before a handler is reached; the
/// idempotency middleware answers before routing. A client cannot tell which one replied, so the
/// envelope has to be the same shape from all of them — which means one place decides it.
/// </para>
/// <para>
/// <see cref="For(Error)"/> is the answer whenever an <see cref="Error"/> exists, and
/// <see cref="KindForStatus(int)"/> is the fallback for the seams that write a failure no
/// <see cref="Error"/> was ever constructed for.
/// </para>
/// </remarks>
internal static class ProblemEnvelope
{
    /// <summary>The <c>code</c> and <c>kind</c> for an error.</summary>
    /// <remarks>
    /// A coded transport fault names its own kind: the payload is the transport's word about
    /// what happened, and the outer <c>transport-fault</c> envelope would hide it.
    /// </remarks>
    public static (string Code, string Kind) For(Error error) =>
        error is Error.TransportFault { Fault: ICodedTransportFault coded }
            ? (error.Code, coded.Kind)
            : (error.Code, ToWireKind(error));

    /// <summary>
    /// The wire slug for a response whose failure was never expressed as an <see cref="Error"/>
    /// — malformed request bytes, or a middleware that rejects a request before routing.
    /// </summary>
    /// <remarks>
    /// Use this only when there is genuinely no error to ask. When one exists, <see cref="For(Error)"/>
    /// is the answer even if the status was remapped: <c>MapError</c> moves where a failure lands
    /// on the wire, it does not change what the failure was.
    /// </remarks>
    public static string KindForStatus(int status) => status switch
    {
        400 => "bad-request",
        401 => "unauthorized",
        403 => "forbidden",
        404 => "not-found",
        405 => "method-not-allowed",
        406 => "not-acceptable",
        409 => "conflict",
        410 => "gone",
        412 => "precondition-failed",
        413 => "content-too-large",
        415 => "unsupported-media-type",
        416 => "range-not-satisfiable",
        422 => "unprocessable-content",
        428 => "precondition-required",
        429 => "too-many-requests",
        501 => "not-implemented",
        503 => "service-unavailable",
        >= 500 => "internal-server-error",
        _ => "error",
    };

    /// <summary>
    /// The <c>type</c> URI ASP.NET Core assigns a problem of this status by default, or
    /// <see langword="null"/> where the framework has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from the framework rather than restated here, so the two cannot drift. A seam that
    /// hard-codes <c>about:blank</c> answers the same status differently from the writer that
    /// resolves it, and a client cannot tell which layer replied — the drift this class exists
    /// to remove, one member above the envelope.
    /// </para>
    /// <para>
    /// A <see langword="null"/> result means <c>type</c> must be <em>omitted</em>. Several
    /// statuses Trellis emits (429, 428, 451, 431, 423, 424) have no framework default;
    /// RFC 9457 §3.1.1 makes an absent <c>type</c> equivalent to <c>about:blank</c>, whereas
    /// writing a bare kind slug would put a non-URI token in a member declared to be a URI
    /// reference.
    /// </para>
    /// <para>
    /// This is the framework <em>default</em> for the status. An application that registers
    /// <c>AddProblemDetails(o =&gt; o.CustomizeProblemDetails = ...)</c> can rewrite the root
    /// problem's <c>type</c>, and that customization is deliberately not replayed here:
    /// <c>ProblemDetailsContext</c> describes the <em>response</em> (it carries the
    /// <c>HttpContext</c>, the triggering exception, and endpoint metadata), so invoking it once
    /// per nested child would stamp children with root-scoped values and let it overwrite each
    /// child's own <c>Status</c> and <c>Instance</c>.
    /// </para>
    /// <para>
    /// Caching by status alone is sound because the lookup takes no other input; it is bounded
    /// because <c>ErrorStatusCodeResolver</c> only ever resolves a status in 100–599.
    /// </para>
    /// </remarks>
    public static string? ProblemTypeForStatus(int status) =>
        _problemTypeByStatus.GetOrAdd(
            status,
            static s => (Results.Problem(statusCode: s) as ProblemHttpResult)?.ProblemDetails.Type);

    private static readonly ConcurrentDictionary<int, string?> _problemTypeByStatus = new();

    /// <summary>Writes the envelope members into a Problem Details extension bag.</summary>
    public static void Apply(IDictionary<string, object?> extensions, string code, string kind)
    {
        extensions["code"] = code;
        extensions["kind"] = kind;
    }

    /// <summary>
    /// Fills in whichever envelope members are absent, leaving any already present untouched.
    /// </summary>
    /// <remarks>
    /// Each member is tested on its own. Guarding both behind a single condition treats a
    /// document carrying only one of them as already enveloped, which leaves the response one
    /// member short of the invariant that every failure carries both.
    /// </remarks>
    public static void Seed(IDictionary<string, object?> extensions, string code, string kind)
    {
        if (!extensions.ContainsKey("code"))
            extensions["code"] = code;

        if (!extensions.ContainsKey("kind"))
            extensions["kind"] = kind;
    }

    /// <summary>
    /// The envelope for a rejected value: an <see cref="Error.InvalidInput"/> is still an error,
    /// so it names its own kind rather than borrowing the status it happens to land on.
    /// </summary>
    public static Dictionary<string, object?> ForError(Error error)
    {
        var (code, kind) = For(error);
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal);
        Apply(extensions, code, kind);
        return extensions;
    }

    /// <summary>
    /// The envelope for a failure no <see cref="Error"/> was constructed for, which therefore
    /// has nothing finer to report than the HTTP condition itself.
    /// </summary>
    public static Dictionary<string, object?> ForStatus(int status)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal);
        Apply(extensions, ValidationCodes.Unspecified, KindForStatus(status));
        return extensions;
    }

    private static string ToWireKind(Error error) => error switch
    {
        Error.InvalidInput => "unprocessable-content",
        Error.InvariantViolation => "unprocessable-content",
        Error.AuthenticationRequired => "unauthorized",
        Error.RateLimited => "too-many-requests",
        Error.Unavailable => "service-unavailable",
        Error.Unexpected u when u.Code == FaultCodes.NotImplemented => "not-implemented",
        Error.Unexpected => "internal-server-error",
        Error.Aggregate => "multi",
        _ => error.Kind,
    };
}
