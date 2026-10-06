namespace Trellis.Authorization;

/// <summary>
/// Identifies a message that participates in the authorization dispatch pipeline.
/// </summary>
/// <remarks>
/// Inherited by the static and resource authorization contracts. This marker alone
/// does not authenticate an actor or declare an authorization policy.
/// </remarks>
public interface IAuthorizationMessage;
