namespace Trellis.Asp;

using System;

/// <summary>
/// Declares where validation failures that reach this endpoint without a location of their own
/// came from, so they project as that location rather than <c>unknown</c>.
/// </summary>
/// <remarks>
/// <para>
/// A domain producer names the field that failed but cannot know where the value came from: the
/// same aggregate method is reachable from a worker, a message handler, or a test, so it emits
/// <see cref="InputLocation.Unspecified"/> rather than asserting a checkable claim that may be
/// false. The endpoint is the first place in the pipeline that knows, which is why the answer is
/// declared there rather than guessed at the boundary.
/// </para>
/// <para>
/// The declaration only fills a gap; it never overwrites. A violation the producer already
/// located — a route, query or header parameter, or a body pointer — passes through untouched,
/// so applying this to an endpoint that mixes bound sources cannot relabel the parameters the
/// model binder stamped.
/// </para>
/// <para>
/// <b>The nearest declaration wins.</b> Applied to a controller it covers every action, and an
/// action that disagrees overrides it by declaring its own — because MVC appends action metadata
/// after controller metadata and the endpoint's last declaration is the one read. That is what
/// makes a controller-wide default safe: a body-bound controller can declare
/// <see cref="InputLocation.Body"/> once, and its one <c>GET</c> action can answer
/// <see cref="InputLocation.Query"/> for itself. Minimal APIs get the same precedence from
/// convention order, so a route group's declaration is overridden by an endpoint's.
/// </para>
/// <para>
/// Declaring <see cref="InputLocation.Unspecified"/> opts an endpoint out of an enclosing
/// declaration, restoring the default of projecting as <c>unknown</c>.
/// </para>
/// <para>
/// Only <see cref="InputLocation.Body"/>, <see cref="InputLocation.Query"/> and
/// <see cref="InputLocation.Unspecified"/> may be declared. There is no evidence yet that a
/// domain producer raises unlocated violations about route or header values, and a declaration
/// nobody can justify is a claim nobody checks.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [ApiController]
/// [InputOrigin(InputLocation.Body)]
/// public sealed class AccountsController : ControllerBase
/// {
///     [HttpGet]
///     [InputOrigin(InputLocation.Query)]
///     public ActionResult&lt;PageResponse&gt; List([FromQuery] string? cursor) => ...
///
///     [HttpPost("{id:AccountId}/deposit")]
///     public Task&lt;ActionResult&lt;AccountResponse&gt;&gt; Deposit(AccountId id, [FromBody] DepositRequest request) => ...
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class InputOriginAttribute : Attribute
{
    /// <summary>
    /// Initializes a new declaration for the supplied location.
    /// </summary>
    /// <param name="location">
    /// Where this endpoint's otherwise-unlocated violations came from. Must be
    /// <see cref="InputLocation.Body"/>, <see cref="InputLocation.Query"/>, or
    /// <see cref="InputLocation.Unspecified"/> to opt out of an enclosing declaration.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="location"/> is <see cref="InputLocation.Path"/>,
    /// <see cref="InputLocation.Header"/>, or not a defined <see cref="InputLocation"/>.
    /// </exception>
    public InputOriginAttribute(InputLocation location)
    {
        if (location is not (InputLocation.Unspecified or InputLocation.Body or InputLocation.Query))
        {
            throw new ArgumentOutOfRangeException(
                nameof(location),
                location,
                "An endpoint may declare only Body, Query, or Unspecified as the origin of its unlocated violations.");
        }

        Location = location;
    }

    /// <summary>
    /// Gets the location this endpoint declares for violations that arrive without one.
    /// </summary>
    public InputLocation Location { get; }
}
