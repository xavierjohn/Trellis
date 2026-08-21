namespace Trellis.Asp;

using System;

/// <summary>
/// Overrides where validation failures that reach this endpoint without a location of their own
/// are said to have come from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Most endpoints do not need this.</b> A domain producer names the field that failed but
/// cannot know where the value came from — the same aggregate method is reachable from a worker,
/// a message handler, or a test — so it emits <see cref="InputLocation.Unspecified"/> rather than
/// asserting a checkable claim that may be false. The response boundary resolves those from the
/// endpoint's own binding map, which the framework already builds: a violation naming one of the
/// endpoint's route parameters projects as <c>path</c>, one naming a query parameter it binds
/// projects as <c>query</c>, and anything left over projects as <c>body</c> when the endpoint
/// binds a body at all. That is evidence rather than inference, and it is read identically for a
/// controller action and a minimal API endpoint, so the two hosts cannot drift apart on the wire.
/// </para>
/// <para>
/// This attribute exists for the cases derivation cannot reach:
/// </para>
/// <list type="bullet">
/// <item><description>
/// an application that never registered ApiExplorer, where nothing can be derived and
/// <see cref="InputLocation.Body"/> or <see cref="InputLocation.Query"/> supplies the residual;
/// </description></item>
/// <item><description>
/// an endpoint whose derived residual is wrong — it binds a body, but its unlocated violations
/// describe something else;
/// </description></item>
/// <item><description>
/// <see cref="InputLocation.Unspecified"/>, to opt out and keep projecting <c>unknown</c>.
/// </description></item>
/// </list>
/// <para>
/// It only ever supplies the <i>residual</i> — the names the URL does not account for. The URL
/// still wins: a violation naming a route or query parameter is located from the binding map
/// whatever this says, because <c>POST /employee/{employeeId}</c> carrying a body can reject both
/// the id in the URL and a member of the payload, and one declaration cannot describe both.
/// </para>
/// <para>
/// It never overwrites either. A violation the producer already located — a route, query or
/// header parameter, or a body pointer — passes through untouched.
/// </para>
/// <para>
/// <b>The nearest declaration wins.</b> Applied to a controller it covers every action, and an
/// action that disagrees overrides it by declaring its own, because MVC appends action metadata
/// after controller metadata and the endpoint's last declaration is the one read. Minimal APIs
/// get the same precedence from convention order, so a route group's declaration is overridden by
/// an endpoint's.
/// </para>
/// <para>
/// Only <see cref="InputLocation.Body"/>, <see cref="InputLocation.Query"/> and
/// <see cref="InputLocation.Unspecified"/> may be declared. Route and header values are already
/// settled by evidence, and a declaration nobody can justify is a claim nobody checks.
/// </para>
/// <para>
/// Derivation gets one case wrong, which is the main reason to reach for an override: a body
/// member sharing a name with a route or query parameter — <c>PUT /employee/{id}</c> carrying
/// <c>{"id": …}</c> — where the URL is evidence about a different value of the same name.
/// Confirming that would mean reflecting over the body type's members, which the AOT-friendly
/// projection path deliberately avoids.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Only needed because this endpoint's residual is not what its binding map implies.
/// [HttpPost("{id:AccountId}/reconcile")]
/// [InputOrigin(InputLocation.Unspecified)]
/// public Task&lt;ActionResult&lt;AccountResponse&gt;&gt; Reconcile(AccountId id) => ...
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
