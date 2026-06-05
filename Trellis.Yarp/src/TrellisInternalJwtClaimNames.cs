namespace Trellis.Yarp;

/// <summary>
/// Canonical JWT claim names that pair the gateway-side minter
/// (<see cref="TrellisActorJwtMinter"/>) with the consumer-side
/// <c>TrellisInternalJwtActorProvider</c> in <c>Trellis.Asp.Authorization</c>.
/// </summary>
/// <remarks>
/// <para>
/// These names match the default <c>TrellisInternalJwtActorOptions</c> values on the
/// consumer side. Centralizing them in a single class minimizes the risk of operational
/// drift between the gateway operator and the downstream service operator — both
/// sides reference these literals (the gateway hard-codes them in the minter; the
/// consumer reads its defaults from <c>TrellisInternalJwtActorOptions</c>) so any
/// future contract version bump is a single coordinated change.
/// </para>
/// <para>
/// Consumers who need different claim names MUST configure both the gateway-side
/// minter AND the downstream <c>TrellisInternalJwtActorOptions</c> in lock-step. v1
/// does not expose claim-name overrides on the gateway side — the contract is the
/// contract.
/// </para>
/// </remarks>
internal static class TrellisInternalJwtClaimNames
{
    /// <summary>
    /// JWT <c>sub</c> claim (the registered subject claim). Carries the namespaced
    /// actor identifier produced by <see cref="TrellisActorForwardingOptions.ActorIdResolver"/>.
    /// </summary>
    public const string Subject = "sub";

    /// <summary>
    /// JWT <c>jti</c> claim (the registered token-identifier claim). Fresh per token
    /// (cryptographically-random GUID-N) so audit pipelines can correlate every minted
    /// JWT to a single mint event without leaking actor identity.
    /// </summary>
    public const string JwtId = "jti";

    /// <summary>
    /// Per-actor authorization-grant claim. Emitted multi-valued (one JSON-array entry
    /// per permission) — NEVER comma-joined or JSON-stringified, per the strict-shape
    /// contract enforced by the consumer side.
    /// </summary>
    public const string Permissions = "permissions";

    /// <summary>
    /// Per-actor deny-set claim. Emitted multi-valued. The deny-overrides-allow
    /// contract invariant requires the matching <see cref="ForbiddenPermissionsCount"/>
    /// claim to ALWAYS be emitted (even when the set is empty) so the consumer can
    /// distinguish "evaluated to empty" from "stripped by a misbehaving proxy."
    /// </summary>
    public const string ForbiddenPermissions = "forbidden_permissions";

    /// <summary>
    /// Sentinel claim asserting which version of the internal-JWT contract this token
    /// conforms to. v1 emits the literal <c>"1"</c>.
    /// </summary>
    public const string ContractVersion = "trellis_actor_contract_version";

    /// <summary>
    /// Decimal-string count of <see cref="Permissions"/> claims emitted in the same
    /// token. Always emitted (including <c>"0"</c> for empty sets) so the consumer
    /// can fail closed when a proxy strips the multi-valued permission claims.
    /// </summary>
    public const string PermissionsCount = "trellis_permissions_count";

    /// <summary>
    /// Decimal-string count of <see cref="ForbiddenPermissions"/> claims emitted in the
    /// same token. Always emitted (including <c>"0"</c> for empty sets) so the consumer
    /// can detect the privilege-escalation footgun where a malicious proxy strips the
    /// deny set silently. Security review finding #5 (Blocking).
    /// </summary>
    public const string ForbiddenPermissionsCount = "trellis_forbidden_permissions_count";

    /// <summary>
    /// The contract version value emitted by v1.
    /// </summary>
    public const string CurrentContractVersion = "1";
}
