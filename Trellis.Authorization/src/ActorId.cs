namespace Trellis.Authorization;

using Trellis;

/// <summary>
/// Strongly-typed identifier for an authenticated principal (the value behind
/// <see cref="Actor.Id"/>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ActorId"/> wraps the raw claim string (typically the JWT <c>sub</c> or AAD
/// <c>oid</c>) so that authorization-layer APIs can accept and return a domain type instead
/// of an untyped <see cref="string"/>. Consumers that store the actor's identity at
/// aggregate boundaries (e.g. <c>Order.CreatedByActorId</c>) should reuse this type to
/// keep the principal-identity concept consistent across the framework, audit fields, and
/// any cross-aggregate comparison that asks "did the same actor do both of these things?".
/// </para>
/// <para>
/// Domain identifiers (a customer ID, a tenant member ID, a domain user aggregate's primary
/// key) are <i>not</i> the same concept as a principal ID. Those remain whatever VO the
/// domain models. Use <see cref="ActorId"/> for authentication-layer principal identity
/// only — what the auth pipeline produced — and resolve to domain identity at the
/// application service boundary if the two differ.
/// </para>
/// <para>
/// Uses <c>[Trim, NotDefault]</c> on top of <see cref="RequiredString{TSelf}"/> so the value
/// is trimmed on construction and an empty or whitespace-only id is rejected. This matches
/// the original <see cref="Actor"/> constructor's <c>ArgumentException.ThrowIfNullOrWhiteSpace</c>
/// guard; the trim is a deliberate, documented normalization step on top of that.
/// </para>
/// </remarks>
[Trim, NotDefault]
public sealed partial class ActorId : RequiredString<ActorId>;
