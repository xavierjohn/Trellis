namespace Trellis.Asp.Authorization;

using System.Collections.Generic;
using System.Collections.Frozen;
using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="TrellisInternalJwtActorOptions"/> at host start. Catches
/// claim-name collisions, missing required-attribute mappings, and the
/// registered-JWT-claim-name privilege-escalation footgun before the first request.
/// </summary>
/// <remarks>
/// Registered with <c>services.AddOptions&lt;TrellisInternalJwtActorOptions&gt;()
/// .ValidateOnStart()</c>. Failing validation throws
/// <see cref="OptionsValidationException"/> at host startup.
/// </remarks>
internal sealed class TrellisInternalJwtActorOptionsValidator
    : IValidateOptions<TrellisInternalJwtActorOptions>
{
    /// <summary>
    /// JWT registered claim names rejected as permission / forbidden-permission / attribute
    /// sources unless <see cref="TrellisInternalJwtActorOptions.UnsafeAllowRegisteredClaimNames"/>
    /// is set. These claim values are gateway- or framework-controlled and treating them as
    /// permission grants would silently elevate a misconfigured consumer. Comparison is
    /// case-INsensitive because <see cref="System.Security.Claims.ClaimsIdentity.FindFirst(string)"/>
    /// matches claim types case-insensitively at runtime — a case-variant configured name
    /// (e.g. "ISS") would still resolve to the canonical claim and bypass an ordinal-only
    /// startup guard.
    /// </summary>
    private static readonly FrozenSet<string> ReservedJwtClaimNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "iss", "aud", "exp", "nbf", "iat", "jti", "sub",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TrellisInternalJwtActorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AuthenticationScheme))
            failures.Add($"{nameof(options.AuthenticationScheme)} must be a non-empty scheme name (typically \"Bearer\").");

        if (string.IsNullOrWhiteSpace(options.ActorIdClaim))
            failures.Add($"{nameof(options.ActorIdClaim)} must be a non-empty claim name.");

        if (string.IsNullOrWhiteSpace(options.PermissionsClaim))
            failures.Add($"{nameof(options.PermissionsClaim)} must be a non-empty claim name.");

        if (string.IsNullOrWhiteSpace(options.ForbiddenPermissionsClaim))
            failures.Add($"{nameof(options.ForbiddenPermissionsClaim)} must be a non-empty claim name.");

        if (string.IsNullOrWhiteSpace(options.ContractVersionClaim))
            failures.Add($"{nameof(options.ContractVersionClaim)} must be a non-empty claim name.");

        if (string.IsNullOrWhiteSpace(options.PermissionsCountClaim))
            failures.Add($"{nameof(options.PermissionsCountClaim)} must be a non-empty claim name.");

        if (string.IsNullOrWhiteSpace(options.ForbiddenPermissionsCountClaim))
            failures.Add($"{nameof(options.ForbiddenPermissionsCountClaim)} must be a non-empty claim name.");

        if (string.IsNullOrWhiteSpace(options.ExpectedContractVersion))
            failures.Add($"{nameof(options.ExpectedContractVersion)} must be a non-empty version literal.");

        // VaryByHeaders is the cache-correctness contract for VaryForActor() consumers; empty
        // would silently let an intermediate cache serve actor A's response to actor B.
        if (options.VaryByHeaders is null || options.VaryByHeaders.Count == 0)
            failures.Add($"{nameof(options.VaryByHeaders)} must contain at least one header name (typically \"Authorization\").");
        else
        {
            foreach (var header in options.VaryByHeaders)
                if (string.IsNullOrWhiteSpace(header))
                    failures.Add($"{nameof(options.VaryByHeaders)} entries must be non-empty header names.");
        }

        // Per-target claim-name collisions. The set of "structural" claim names the provider
        // reads is disjoint by design: an attribute claim that shadows the actor-id claim
        // (for example) would produce ambiguous semantics depending on lookup order.
        var structuralClaimNames = new[]
        {
            options.ActorIdClaim,
            options.PermissionsClaim,
            options.ForbiddenPermissionsClaim,
            options.ContractVersionClaim,
            options.PermissionsCountClaim,
            options.ForbiddenPermissionsCountClaim,
        };
        var seenStructural = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name2 in structuralClaimNames)
        {
            if (string.IsNullOrWhiteSpace(name2))
                continue; // Already reported above.
            if (!seenStructural.Add(name2))
                failures.Add($"Duplicate claim name '{name2}' across structural option slots " +
                             $"({nameof(options.ActorIdClaim)} / {nameof(options.PermissionsClaim)} / " +
                             $"{nameof(options.ForbiddenPermissionsClaim)} / {nameof(options.ContractVersionClaim)} / " +
                             $"{nameof(options.PermissionsCountClaim)} / {nameof(options.ForbiddenPermissionsCountClaim)}). " +
                             "Each structural slot must read from a distinct claim type.");
        }

        // AttributeClaimMap shape + collision rules.
        if (options.AttributeClaimMap is not null)
        {
            var seenAttributeClaimTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (attrName, claimType) in options.AttributeClaimMap)
            {
                if (string.IsNullOrWhiteSpace(attrName))
                    failures.Add($"{nameof(options.AttributeClaimMap)} contains an entry with a null or empty attribute name; remove it or assign a non-empty key.");
                if (string.IsNullOrWhiteSpace(claimType))
                {
                    failures.Add($"{nameof(options.AttributeClaimMap)}['{attrName}'] is mapped to a null or empty claim type; set a non-empty claim name or remove the entry.");
                    continue;
                }

                if (!seenAttributeClaimTypes.Add(claimType))
                    failures.Add($"{nameof(options.AttributeClaimMap)} contains a duplicate claim type '{claimType}' across multiple attribute names. Each attribute must read from a distinct claim type to avoid ambiguous semantics.");

                if (seenStructural.Contains(claimType))
                    failures.Add($"{nameof(options.AttributeClaimMap)}['{attrName}'] is mapped to claim type '{claimType}' which is also configured as a structural claim ({nameof(options.ActorIdClaim)} / {nameof(options.PermissionsClaim)} / etc.). Attribute claims must be distinct from structural claims so the provider can read each unambiguously.");

                if (!options.UnsafeAllowRegisteredClaimNames && ReservedJwtClaimNames.Contains(claimType))
                    failures.Add($"{nameof(options.AttributeClaimMap)}['{attrName}'] maps to reserved JWT claim '{claimType}'. Reserved claims (iss/aud/exp/nbf/iat/jti/sub) are gateway-controlled — reading them as attributes risks privilege escalation. Set {nameof(options.UnsafeAllowRegisteredClaimNames)} = true to waive this check.");
            }
        }

        // Reserved-claim check for permissions / forbidden permissions.
        if (!options.UnsafeAllowRegisteredClaimNames)
        {
            if (!string.IsNullOrWhiteSpace(options.PermissionsClaim)
                && ReservedJwtClaimNames.Contains(options.PermissionsClaim))
                failures.Add($"{nameof(options.PermissionsClaim)} is set to reserved JWT claim '{options.PermissionsClaim}'. Reserved claims (iss/aud/exp/nbf/iat/jti/sub) are gateway-controlled — reading them as permissions risks privilege escalation. Set {nameof(options.UnsafeAllowRegisteredClaimNames)} = true to waive this check.");

            if (!string.IsNullOrWhiteSpace(options.ForbiddenPermissionsClaim)
                && ReservedJwtClaimNames.Contains(options.ForbiddenPermissionsClaim))
                failures.Add($"{nameof(options.ForbiddenPermissionsClaim)} is set to reserved JWT claim '{options.ForbiddenPermissionsClaim}'. Reserved claims (iss/aud/exp/nbf/iat/jti/sub) are gateway-controlled — reading them as deny-permissions risks privilege escalation. Set {nameof(options.UnsafeAllowRegisteredClaimNames)} = true to waive this check.");
        }

        // RequiredAttributes consistency. Use the map's own comparer so the dedup + lookup
        // here agrees with the runtime — otherwise a case-insensitive map could pass startup
        // validation but be treated as missing-from-map at runtime. Additionally, require
        // ORDINAL-exact match against AttributeClaimMap keys (regardless of map comparer)
        // so that the Actor.Attributes (ordinal-keyed FrozenDictionary) is queryable under
        // the same spelling the consumer used in RequiredAttributes — otherwise a case-
        // insensitive map could enforce a required attribute but emit it under a different
        // spelling, causing downstream lookups to silently miss.
        if (options.RequiredAttributes is not null)
        {
            var mapComparer = options.AttributeClaimMap?.Comparer ?? StringComparer.Ordinal;
            var seenRequired = new HashSet<string>(mapComparer);
            foreach (var required in options.RequiredAttributes)
            {
                if (string.IsNullOrWhiteSpace(required))
                {
                    failures.Add($"{nameof(options.RequiredAttributes)} contains a null or empty entry; remove it or assign a non-empty attribute name.");
                    continue;
                }

                if (!seenRequired.Add(required))
                    failures.Add($"{nameof(options.RequiredAttributes)} contains the duplicate entry '{required}'; each required attribute must be listed at most once.");
                if (options.AttributeClaimMap is null || !options.AttributeClaimMap.ContainsKey(required))
                {
                    failures.Add($"{nameof(options.RequiredAttributes)} contains '{required}' but {nameof(options.AttributeClaimMap)} does not map it to any claim type. Add a {nameof(options.AttributeClaimMap)} entry for '{required}' or remove it from {nameof(options.RequiredAttributes)}.");
                }
                else if (!options.AttributeClaimMap.Keys.Contains(required, StringComparer.Ordinal))
                {
                    failures.Add($"{nameof(options.RequiredAttributes)} contains '{required}' but {nameof(options.AttributeClaimMap)} only has a case-variant key (the map uses a case-insensitive comparer). {nameof(options.RequiredAttributes)} entries must ORDINAL-exactly match an {nameof(options.AttributeClaimMap)} key so the resulting Actor.Attributes (ordinal-keyed) exposes them under the configured spelling.");
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
