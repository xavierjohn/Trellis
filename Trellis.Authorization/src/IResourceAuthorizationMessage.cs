namespace Trellis.Authorization;

/// <summary>
/// Identifies a message whose dispatch requires successful resource authorization.
/// </summary>
/// <remarks>
/// Inherited by direct and indirect resource authorization contracts. Static
/// permissions are optional and are declared separately through <see cref="IAuthorize"/>.
/// </remarks>
public interface IResourceAuthorizationMessage : IAuthorizationMessage;
