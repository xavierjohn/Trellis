namespace Trellis.FluentValidation;

using System;
using System.Collections.Generic;

/// <summary>
/// Widens the per-validator allowlist that <see cref="ValidationArgsProjection"/> applies, so an
/// application can publish a machine-readable operand Trellis withholds by default.
/// </summary>
/// <remarks>
/// <para>
/// The default allowlist is deliberately narrow: it carries only the operands whose meaning Trellis
/// can vouch for across every validator that populates them. FluentValidation populates
/// placeholders its message never uses and populates them with sentinels, so a blanket
/// pass-through would put <c>maxLength: -1</c> on the wire for a <c>MinimumLength</c> failure.
/// Widening is therefore a per-validator decision the application makes with knowledge Trellis does
/// not have.
/// </para>
/// <para>
/// <b>This only ever widens.</b> There is no method to remove an entry, because the default set is
/// the conservative one — a remove operation would only ever narrow a client contract that
/// something already depends on, and an application that wants fewer args can stop reading them.
/// </para>
/// <para>
/// <b>Widening does not bypass the safety gates.</b> An opted-in placeholder still passes through
/// the containment gate, the bound, and the control-character escaping in
/// <see cref="ValidationArgsProjection"/>. It cannot re-admit <c>PropertyValue</c> or
/// <c>PropertyPath</c>: the first carries the user's submitted input verbatim, which is a
/// disclosure and PII hazard no opt-in should be able to switch on, and the second carries the
/// traversal path that the violation's own location already reports.
/// </para>
/// <para>
/// Register through the options system so both the standalone helpers and the Mediator pipeline
/// adapter see the same configuration:
/// </para>
/// <code>
/// services.Configure&lt;ValidationArgsOptions&gt;(options =&gt;
///     options.AllowArgs("PredicateValidator", "MinAge"));
/// </code>
/// </remarks>
public sealed class ValidationArgsOptions
{
    /// <summary>
    /// Placeholders no opt-in can re-admit, each with the reason it is denied.
    /// </summary>
    private static readonly Dictionary<string, string> NeverAllowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PropertyValue"] = "it carries the submitted input verbatim, which is a disclosure and PII hazard. Report the value's location instead, which the violation already does.",
        ["PropertyPath"] = "it carries the traversal path the violation's own location already reports. Read the violation's pointer instead.",
    };

    private readonly Dictionary<string, HashSet<string>> additional = new(StringComparer.Ordinal);

    private readonly bool isShared;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationArgsOptions"/> class.
    /// </summary>
    public ValidationArgsOptions()
    {
    }

    private ValidationArgsOptions(bool isShared) => this.isShared = isShared;

    /// <summary>
    /// The configuration used when an application registered none.
    /// </summary>
    /// <remarks>
    /// This instance is shared process-wide, so it rejects <see cref="AllowArgs"/>. Widening it
    /// would silently widen every validation call in the process, including those in unrelated
    /// scopes and on other threads — a global effect from what reads like a local one. Configure
    /// through the options system instead, which hands out an instance of your own.
    /// </remarks>
    public static ValidationArgsOptions Default { get; } = new(isShared: true);

    /// <summary>
    /// Allows the named placeholders to be emitted for failures carrying
    /// <paramref name="errorCode"/>.
    /// </summary>
    /// <param name="errorCode">
    /// FluentValidation's error code for the validator, such as <c>"PredicateValidator"</c> or a
    /// code the application set with <c>WithErrorCode</c>.
    /// </param>
    /// <param name="placeholderNames">
    /// The placeholder names as FluentValidation spells them, in <c>PascalCase</c>. They reach the
    /// wire in <c>camelCase</c>, matching every other arg.
    /// </param>
    /// <returns>The same instance, so calls can be chained.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="errorCode"/> is blank, or when <paramref name="placeholderNames"/>
    /// names a placeholder that can never be allowed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when called on <see cref="Default"/>, which is shared process-wide.
    /// </exception>
    public ValidationArgsOptions AllowArgs(string errorCode, params string[] placeholderNames)
    {
        if (isShared)
        {
            throw new InvalidOperationException(
                $"{nameof(ValidationArgsOptions)}.{nameof(Default)} is shared across the process and cannot be widened. Register your own through services.Configure<{nameof(ValidationArgsOptions)}>(...) so the change is scoped to the application that made it.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentNullException.ThrowIfNull(placeholderNames);

        foreach (var name in placeholderNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            // Failing loudly beats silently dropping the name: an application that asked for
            // PropertyValue has misunderstood what args are for, and a silent drop would leave it
            // waiting for an arg that is never coming.
            if (NeverAllowed.TryGetValue(name, out var reason))
            {
                throw new ArgumentException(
                    $"'{name}' can never be emitted as a validation arg because {reason}",
                    nameof(placeholderNames));
            }

            if (!additional.TryGetValue(errorCode, out var names))
            {
                names = new HashSet<string>(StringComparer.Ordinal);
                additional[errorCode] = names;
            }

            names.Add(name);
        }

        return this;
    }

    /// <summary>
    /// Gets the placeholders the application added for an error code, or an empty set.
    /// </summary>
    internal IReadOnlyCollection<string> AdditionalFor(string errorCode) =>
        additional.TryGetValue(errorCode, out var names) ? names : [];

    /// <summary>
    /// Gets whether the application widened any error code, letting the projection keep its fast
    /// path when nothing was configured.
    /// </summary>
    internal bool IsEmpty => additional.Count == 0;
}
