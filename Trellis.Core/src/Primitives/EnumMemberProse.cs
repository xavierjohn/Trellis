namespace Trellis;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Renders a permitted-member list for a human-readable message, under the same bound that governs
/// the machine-readable <c>allowed</c> arg.
/// </summary>
/// <remarks>
/// <para>
/// The prose and the arg are gated by one constant deliberately. Capping only the arg would leave
/// the larger half of the problem in place: <c>RequiredEnum.TryCreate</c> spells every member into
/// its detail, which for the 248 ISO country names is roughly 2.8 KB of prose on every rejection —
/// more than the arg it was meant to replace.
/// </para>
/// <para>
/// Above the bound the clause is dropped rather than shortened, matching the arg. A message reading
/// "Valid values: Afghanistan, Albania, …" invites a reader to believe the list is exhaustive, and
/// a reader who cannot see the whole set is better served by being told nothing than by being told
/// a fraction that looks complete. The resulting sentence is the one the body converter already
/// emits, so the producers agree there too.
/// </para>
/// </remarks>
internal static class EnumMemberProse
{
    /// <summary>
    /// The members joined in ordinal order, or <see langword="null"/> when there are more than
    /// <see cref="ValidationArgs.MaxAllowedMembers"/> of them.
    /// </summary>
    internal static string? ListOrNull(IReadOnlyCollection<string> names) =>
        names.Count > ValidationArgs.MaxAllowedMembers
            ? null
            : string.Join(", ", names.OrderBy(name => name, StringComparer.Ordinal));
}
