namespace Trellis.Testing;

using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Primitives;
using Trellis.Authorization;

/// <summary>Extension methods to enable FluentAssertions on <see cref="Actor"/>.</summary>
public static class ActorAssertionsExtensions
{
    /// <summary>Returns actor assertions.</summary>
    /// <param name="actor">The actor under test.</param>
    /// <returns>Assertions for the actor.</returns>
    public static ActorAssertions Should(this Actor? actor) => new(actor!);
}

/// <summary>
/// Assertions over an <see cref="Actor"/> that follow the same rule as the authorization pipeline:
/// an explicitly forbidden permission overrides a granted one.
/// </summary>
public class ActorAssertions : ReferenceTypeAssertions<Actor, ActorAssertions>
{
    /// <summary>Initializes a new instance of the <see cref="ActorAssertions"/> class.</summary>
    /// <param name="subject">The actor under test.</param>
    public ActorAssertions(Actor subject)
        : base(subject)
    {
    }

    /// <inheritdoc/>
    protected override string Identifier => "actor";

    /// <summary>Asserts the actor equals <paramref name="expected"/> using <see cref="Actor.Equals(Actor?)"/>.</summary>
    /// <param name="expected">The expected actor.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<ActorAssertions> Be(Actor expected, string because = "", params object[] becauseArgs)
    {
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Equals(Subject, expected))
            .FailWith("Expected {context:actor} to be {0}{reason}, but found {1}.", expected, Subject);
        return new AndConstraint<ActorAssertions>(this);
    }

    /// <summary>Asserts the actor effectively holds <paramref name="permission"/>.</summary>
    /// <param name="permission">The permission that must be effective.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<ActorAssertions> HavePermission(string permission, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return AssertHasPermission(permission, () => Subject.HasPermission(permission), because, becauseArgs);
    }

    /// <summary>Asserts the actor effectively holds <paramref name="permission"/> within <paramref name="scope"/>.</summary>
    /// <param name="permission">The permission name.</param>
    /// <param name="scope">The scope appended after <see cref="Actor.PermissionScopeSeparator"/>.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<ActorAssertions> HaveScopedPermission(
        string permission, string scope, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(scope);
        return AssertHasPermission(
            $"{permission}{Actor.PermissionScopeSeparator}{scope}",
            () => Subject.HasPermission(permission, scope),
            because,
            becauseArgs);
    }

    /// <summary>Asserts the actor does not effectively hold <paramref name="permission"/> (absent or forbidden).</summary>
    /// <param name="permission">The permission that must not be effective.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<ActorAssertions> NotHavePermission(string permission, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(permission);
        if (RequireSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(!Subject.HasPermission(permission))
                .FailWith("Expected {context:actor} not to have permission {0}{reason}, but it does.", permission);
        }

        return new AndConstraint<ActorAssertions>(this);
    }

    /// <summary>Asserts the actor effectively holds every permission, reporting each one it lacks.</summary>
    /// <param name="permissions">The permissions that must all be effective.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<ActorAssertions> HaveAllPermissions(IEnumerable<string> permissions, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (RequireSubject(because, becauseArgs))
        {
            var lacking = permissions.Where(permission => !Subject.HasPermission(permission)).ToArray();
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(lacking.Length == 0)
                .FailWith("Expected {context:actor} to have all permissions{reason}, but it lacks: {0}.", string.Join(", ", lacking));
        }

        return new AndConstraint<ActorAssertions>(this);
    }

    /// <summary>Asserts the actor effectively holds at least one of the permissions.</summary>
    /// <param name="permissions">The candidate permissions.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<ActorAssertions> HaveAnyPermission(IEnumerable<string> permissions, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (RequireSubject(because, becauseArgs))
        {
            var candidates = permissions.ToArray();
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(candidates.Any(Subject.HasPermission))
                .FailWith("Expected {context:actor} to have any of [{0}]{reason}, but it has none.", string.Join(", ", candidates));
        }

        return new AndConstraint<ActorAssertions>(this);
    }

    /// <summary>Asserts the actor identity equals <paramref name="expected"/>.</summary>
    /// <param name="expected">The expected actor identity.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<ActorAssertions> HaveId(ActorId expected, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (RequireSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(Subject.Id.Equals(expected))
                .FailWith("Expected {context:actor} to have id {0}{reason}, but found {1}.", expected.Value, Subject.Id.Value);
        }

        return new AndConstraint<ActorAssertions>(this);
    }

    /// <summary>Asserts the actor identity value equals <paramref name="expected"/>.</summary>
    /// <param name="expected">The expected identity value.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<ActorAssertions> HaveId(string expected, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (RequireSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(string.Equals(Subject.Id.Value, expected, StringComparison.Ordinal))
                .FailWith("Expected {context:actor} to have id {0}{reason}, but found {1}.", expected, Subject.Id.Value);
        }

        return new AndConstraint<ActorAssertions>(this);
    }

    /// <summary>Asserts the actor owns a resource owned by <paramref name="resourceOwnerId"/>.</summary>
    /// <param name="resourceOwnerId">The resource owner identity.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<ActorAssertions> BeOwnerOf(ActorId resourceOwnerId, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(resourceOwnerId);
        if (RequireSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(Subject.IsOwner(resourceOwnerId))
                .FailWith("Expected {context:actor} to be the owner for {0}{reason}, but its id is {1}.", resourceOwnerId.Value, Subject.Id.Value);
        }

        return new AndConstraint<ActorAssertions>(this);
    }

    /// <summary>Asserts the actor carries the attribute key.</summary>
    /// <param name="key">The attribute key.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<ActorAssertions> HaveAttribute(string key, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (RequireSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(Subject.HasAttribute(key))
                .FailWith("Expected {context:actor} to have attribute {0}{reason}, but it has: {1}.",
                    key, string.Join(", ", Subject.Attributes.Keys));
        }

        return new AndConstraint<ActorAssertions>(this);
    }

    /// <summary>Asserts the actor carries the attribute key with exactly <paramref name="value"/>.</summary>
    /// <param name="key">The attribute key.</param>
    /// <param name="value">The expected attribute value.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<ActorAssertions> HaveAttributeValue(string key, string value, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (RequireSubject(because, becauseArgs))
        {
            var actual = Subject.GetAttribute(key);
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(string.Equals(actual, value, StringComparison.Ordinal))
                .FailWith("Expected {context:actor} attribute {0} to be {1}{reason}, but found {2}.", key, value, actual);
        }

        return new AndConstraint<ActorAssertions>(this);
    }

    private AndConstraint<ActorAssertions> AssertHasPermission(
        string permission, Func<bool> hasPermission, string because, object[] becauseArgs)
    {
        if (RequireSubject(because, becauseArgs))
        {
            var forbidden = Subject.ForbiddenPermissions.Contains(permission);
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(hasPermission())
                .FailWith(
                    "Expected {context:actor} to have permission {0}{reason}, but " +
                    (forbidden ? "it is explicitly forbidden." : "it was not granted."),
                    permission);
        }

        return new AndConstraint<ActorAssertions>(this);
    }

    private bool RequireSubject(string because, object[] becauseArgs) =>
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Subject is not null)
            .FailWith("Expected {context:actor} to not be <null>{reason}.");
}

/// <summary>Assertions for the static permission contract of an <see cref="IAuthorize"/> message.</summary>
/// <remarks>
/// These use the same <see cref="Actor.HasAllPermissions(IEnumerable{string})"/> rule as the Mediator
/// authorization behavior, so denied paths can be tested without dispatching through the pipeline.
/// They are explicit methods rather than a <c>Should()</c> entry point so commands and queries keep
/// binding to FluentAssertions.
/// </remarks>
public static class AuthorizeMessageAssertionsExtensions
{
    /// <summary>Asserts the message requires exactly these permissions, ignoring order.</summary>
    /// <param name="message">The message under test.</param>
    /// <param name="permissions">The exact set of required permissions.</param>
    public static void ShouldRequirePermissions(this IAuthorize message, params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(permissions);
        var actual = message.RequiredPermissions.ToHashSet(StringComparer.Ordinal);
        var expected = permissions.ToHashSet(StringComparer.Ordinal);
        var missing = expected.Except(actual).ToArray();
        var unexpected = actual.Except(expected).ToArray();

        Execute.Assertion
            .ForCondition(missing.Length == 0 && unexpected.Length == 0)
            .FailWith(
                "Expected {0} to require exactly [{1}], but permissions missing: [{2}]; unexpected: [{3}].",
                message.GetType().Name,
                string.Join(", ", expected),
                string.Join(", ", missing),
                string.Join(", ", unexpected));
    }

    /// <summary>Asserts the actor holds all permissions the message requires.</summary>
    /// <param name="message">The message under test.</param>
    /// <param name="actor">The actor that must be authorized.</param>
    public static void ShouldBeAuthorizedFor(this IAuthorize message, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(actor);
        var lacking = message.RequiredPermissions.Where(permission => !actor.HasPermission(permission)).ToArray();

        Execute.Assertion
            .ForCondition(lacking.Length == 0)
            .FailWith(
                "Expected actor {0} to be authorized for {1}, but it lacks: [{2}].",
                actor.Id.Value, message.GetType().Name, string.Join(", ", lacking));
    }

    /// <summary>Asserts the actor is denied because it lacks at least one required permission.</summary>
    /// <param name="message">The message under test.</param>
    /// <param name="actor">The actor that must be denied.</param>
    public static void ShouldNotBeAuthorizedFor(this IAuthorize message, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(actor);

        Execute.Assertion
            .ForCondition(!actor.HasAllPermissions(message.RequiredPermissions))
            .FailWith(
                "Expected actor {0} not to be authorized for {1}, but it holds all required permissions.",
                actor.Id.Value, message.GetType().Name);
    }
}