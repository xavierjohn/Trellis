namespace Trellis.Asp.Authorization;

using Microsoft.AspNetCore.Authentication;

/// <summary>
/// Extension methods for registering the Azure App Service / Container Apps built-in
/// authentication ("Easy Auth") scheme.
/// </summary>
public static class EasyAuthAuthenticationExtensions
{
    /// <summary>
    /// Adds the Easy Auth authentication scheme under
    /// <see cref="EasyAuthDefaults.AuthenticationScheme"/>.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <returns>The authentication builder for chaining.</returns>
    public static AuthenticationBuilder AddEasyAuth(this AuthenticationBuilder builder)
        => builder.AddEasyAuth(EasyAuthDefaults.AuthenticationScheme, static _ => { });

    /// <summary>
    /// Adds the Easy Auth authentication scheme under
    /// <see cref="EasyAuthDefaults.AuthenticationScheme"/> with the supplied options.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="configureOptions">Delegate to customize <see cref="EasyAuthAuthenticationOptions"/>.</param>
    /// <returns>The authentication builder for chaining.</returns>
    public static AuthenticationBuilder AddEasyAuth(
        this AuthenticationBuilder builder,
        Action<EasyAuthAuthenticationOptions> configureOptions)
        => builder.AddEasyAuth(EasyAuthDefaults.AuthenticationScheme, configureOptions);

    /// <summary>
    /// Adds the Easy Auth authentication scheme under the supplied scheme name with the
    /// supplied options.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="authenticationScheme">The scheme name to register.</param>
    /// <param name="configureOptions">Delegate to customize <see cref="EasyAuthAuthenticationOptions"/>.</param>
    /// <returns>The authentication builder for chaining.</returns>
    public static AuthenticationBuilder AddEasyAuth(
        this AuthenticationBuilder builder,
        string authenticationScheme,
        Action<EasyAuthAuthenticationOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddScheme<EasyAuthAuthenticationOptions, EasyAuthAuthenticationHandler>(
            authenticationScheme,
            configureOptions);
    }
}
