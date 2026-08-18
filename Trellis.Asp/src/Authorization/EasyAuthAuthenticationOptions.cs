namespace Trellis.Asp.Authorization;

using Microsoft.AspNetCore.Authentication;

/// <summary>
/// Options for <see cref="EasyAuthAuthenticationHandler"/>. The Easy Auth principal header
/// names are fixed by the Azure platform contract (see <see cref="EasyAuthDefaults"/>) and are
/// not configurable; this type carries only the standard <see cref="AuthenticationSchemeOptions"/>
/// surface (events, forwarding, etc.).
/// </summary>
public sealed class EasyAuthAuthenticationOptions : AuthenticationSchemeOptions
{
}