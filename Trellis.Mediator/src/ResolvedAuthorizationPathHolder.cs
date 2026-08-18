namespace Trellis.Mediator;

/// <summary>
/// Closed-generic carrier for a <see cref="ResolvedAuthorizationPath"/> registered per
/// via-authorized command. Used so DI naturally disambiguates the path per command (each
/// closed generic is a distinct service type) and so
/// <see cref="ResourceAuthorizationViaBehavior{TMessage, TLeaf, TOwner, TResponse}"/> can be
/// registered as a TYPED descriptor (not a factory descriptor), letting
/// <see cref="ServiceCollectionExtensions"/>'s relocator recognize it by
/// <see cref="Microsoft.Extensions.DependencyInjection.ServiceDescriptor.ImplementationType"/> alone.
/// </summary>
/// <typeparam name="TMessage">The command type.</typeparam>
/// <typeparam name="TLeaf">The leaf resource type.</typeparam>
/// <typeparam name="TOwner">The owner resource type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class ResolvedAuthorizationPathHolder<TMessage, TLeaf, TOwner, TResponse>
{
    /// <summary>Creates a new holder for the resolved path.</summary>
    /// <param name="path">The pre-built path. Must not be null, and its
    /// <see cref="ResolvedAuthorizationPath.MessageType"/>, <see cref="ResolvedAuthorizationPath.LeafType"/>,
    /// and <see cref="ResolvedAuthorizationPath.OwnerType"/> must equal
    /// <typeparamref name="TMessage"/>, <typeparamref name="TLeaf"/>, and <typeparamref name="TOwner"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/>'s
    /// <see cref="ResolvedAuthorizationPath.MessageType"/>, <see cref="ResolvedAuthorizationPath.LeafType"/>,
    /// or <see cref="ResolvedAuthorizationPath.OwnerType"/> does not equal the corresponding
    /// holder generic argument. Validating here makes misconfiguration fail fast at registration
    /// (the holder is constructed eagerly by
    /// <c>AddRelatedResourceAuthorization(..., ResolvedAuthorizationPath)</c> and by the assembly
    /// scanner) rather than at first request when
    /// <see cref="ResourceAuthorizationViaBehavior{TMessage, TLeaf, TOwner, TResponse}"/> would
    /// otherwise re-detect the mismatch.
    /// </exception>
    public ResolvedAuthorizationPathHolder(ResolvedAuthorizationPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.MessageType != typeof(TMessage))
            throw new ArgumentException(
                $"ResolvedAuthorizationPath.MessageType is {path.MessageType.Name} but holder is " +
                $"closed over TMessage = {typeof(TMessage).Name}. Each via-authorized command must " +
                $"receive its own ResolvedAuthorizationPath; ensure the path was built for this " +
                $"specific command.",
                nameof(path));

        if (path.LeafType != typeof(TLeaf))
            throw new ArgumentException(
                $"ResolvedAuthorizationPath.LeafType is {path.LeafType.Name} but holder is closed " +
                $"over TLeaf = {typeof(TLeaf).Name}. The path and holder generic arguments must agree " +
                $"on the leaf resource type.",
                nameof(path));

        if (path.OwnerType != typeof(TOwner))
            throw new ArgumentException(
                $"ResolvedAuthorizationPath.OwnerType is {path.OwnerType.Name} but holder is closed " +
                $"over TOwner = {typeof(TOwner).Name}. The path and holder generic arguments must " +
                $"agree on the owner resource type.",
                nameof(path));

        Path = path;
    }

    /// <summary>Gets the resolved authorization path.</summary>
    public ResolvedAuthorizationPath Path { get; }
}