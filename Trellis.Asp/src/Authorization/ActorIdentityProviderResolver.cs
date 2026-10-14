namespace Trellis.Asp.Authorization;

using Trellis.Authorization;

internal static class ActorIdentityProviderResolver
{
    internal static bool Supports(Type type) =>
        typeof(IActorIdentityProvider).IsAssignableFrom(type)
        || type == typeof(ClaimsActorProvider)
        || type == typeof(NestedJsonPathClaimsActorProvider)
        || type == typeof(EntraActorProvider)
        || type == typeof(EasyAuthClaimsActorProvider)
        || type == typeof(DevelopmentActorProvider);

    internal static IActorIdentityProvider Resolve(IActorProvider provider)
    {
        if (provider is IActorIdentityProvider identity)
            return identity;

        return provider switch
        {
            NestedJsonPathClaimsActorProvider nested when provider.GetType() == typeof(NestedJsonPathClaimsActorProvider) =>
                new BuiltInIdentityProvider(nested.ResolveCurrentActorIdAsync),
            EntraActorProvider entra => new BuiltInIdentityProvider(entra.ResolveCurrentActorIdAsync),
            DevelopmentActorProvider development => new BuiltInIdentityProvider(development.ResolveCurrentActorIdAsync),
            EasyAuthClaimsActorProvider easyAuth => new BuiltInIdentityProvider(easyAuth.ResolveCurrentActorIdAsync),
            ClaimsActorProvider claims when provider.GetType() == typeof(ClaimsActorProvider) =>
                new BuiltInIdentityProvider(claims.ResolveCurrentActorIdAsync),
            _ => throw new InvalidOperationException(
                $"Actor provider '{provider.GetType().FullName}' does not support IActorIdentityProvider. " +
                "Implement the identity-only contract with the same canonical mapping as full actor resolution. " +
                "Identity resolution never falls back to loading the full actor."),
        };
    }

    private sealed class BuiltInIdentityProvider(
        Func<CancellationToken, ValueTask<Maybe<ActorId>>> resolve) : IActorIdentityProvider
    {
        public ValueTask<Maybe<ActorId>> GetCurrentActorIdAsync(CancellationToken cancellationToken = default) =>
            resolve(cancellationToken);
    }
}