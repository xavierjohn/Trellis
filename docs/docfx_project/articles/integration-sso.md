---
title: Single Sign-On Integration
package: Trellis.Asp
topics: [sso, oidc, google, entra, microsoft, facebook, auth0, okta, keycloak, jwt, claims, actor, multi-tenant]
related_api_reference: [trellis-api-asp.md, trellis-api-authorization.md, trellis-api-core.md]
last_verified: 2026-05-15
audience: [developer]
---
# Single Sign-On Integration

`Trellis.Asp.Authorization` (shipped inside the `Trellis.Asp` package) turns an authenticated `JwtBearer` principal from **any OIDC issuer** — Google, Microsoft Entra ID, Facebook, Auth0, Okta, Keycloak, or your own OpenID Connect server — into a frozen `Actor` so handlers and endpoints stop parsing JWTs.

This article is the **entry point** for wiring authentication into a Trellis service. For framework-side authorization mechanics (mediator behaviors, resource authorization, the decision tree, cache partitioning) see [ASP.NET Core Authorization](integration-asp-authorization.md).

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Validate any standards-compliant OIDC token (Google, Auth0, Okta, Keycloak, custom) | `AddJwtBearer` + `AddClaimsActorProvider(options?)` | [Generic OIDC](#generic-oidc) |
| Validate Microsoft Entra ID v2.0 tokens with `oid` / `roles` / `tid` / `amr` defaults | `AddJwtBearer` + `AddEntraActorProvider(options?)` | [Microsoft (Entra ID)](#microsoft-entra-id) |
| Validate Google ID tokens | Authority `https://accounts.google.com` + `ValidIssuers` accepting both forms | [Google sign-in](#google-sign-in) |
| Validate Sign in with Apple ID tokens from an iOS/macOS app | Authority `https://appleid.apple.com` + bundle id as `Audience` | [Sign in with Apple (iOS / macOS app)](#sign-in-with-apple-ios--macos-app) |
| Validate Google / Microsoft / OIDC tokens from an Android app | Same as the IdP recipe; client-side flow uses Credential Manager / MSAL / AppAuth | [Android app](#android-app) |
| Validate Facebook tokens via an OIDC bridge | Authority for your bridge (Auth0/Cognito/etc.) + `AddClaimsActorProvider` | [Facebook (via OIDC bridge)](#facebook-via-oidc-bridge) |
| Accept tokens from multiple IdPs in one API (e.g. Microsoft + Google + Apple) | Multiple `AddJwtBearer("<scheme>", ...)` + a default policy listing every scheme | [Multiple IdPs in one API](#multiple-idps-in-one-api) |
| Project nested JSON claims (Keycloak `realm_access.roles`) | Subclass `ClaimsActorProvider` + `AddCachingActorProvider<T>()` | [Keycloak / nested claims](#keycloak--nested-claims) |
| Flatten roles, scopes, or `scp` into application permissions | Override `EntraActorOptions.MapPermissions` or use a custom `IActorProvider` | [Scope and permission extraction](#scope-and-permission-extraction) |
| Accept multi-tenant Entra tokens and pin allowed tenants | `JwtBearerOptions.TokenValidationParameters` + `ActorAttributes.TenantId` | [Multi-tenant Entra](#multi-tenant-entra) |
| Use a fake actor in Development without a real IdP | `AddDevelopmentActorProvider(options?)` + `X-Test-Actor` header | [Development defaults](#development-defaults) |

## Use this guide when

- You front a Trellis service with `JwtBearer` and need a single, predictable `Actor` shape regardless of which OIDC provider issued the token.
- You need one configuration story for Google, Sign in with Apple, Microsoft (Entra), Facebook (via an OIDC bridge), Auth0, Okta, and Keycloak.
- You need to wire an Android, iOS, or macOS app against the Trellis backend.
- You need a **single API** to accept tokens from multiple IdPs at once — for example, a web frontend with Sign in with Microsoft *and* Sign in with Google buttons, plus phone apps signing in with Apple or Google.
- You need a Development-only seam (`X-Test-Actor`) that fail-closes outside `IsDevelopment()`.
- You need to host a multi-tenant Entra app and pin which tenants may call your API.

## Surface at a glance

`Trellis.Asp.Authorization` (namespace inside the `Trellis.Asp` package) exposes one set of DI extensions and four `IActorProvider` implementations.

| API | Kind | Purpose |
|---|---|---|
| `AddClaimsActorProvider(this IServiceCollection, Action<ClaimsActorOptions>?)` | DI extension | Scoped `IActorProvider` → `ClaimsActorProvider`. The general-purpose provider for any flat-claim OIDC token. |
| `AddEntraActorProvider(this IServiceCollection, Action<EntraActorOptions>?)` | DI extension | Scoped `IActorProvider` → `EntraActorProvider` (Microsoft Entra ID v2.0 claim shape with `oid` / `roles` / `tid` / `amr` defaults). |
| `AddDevelopmentActorProvider(this IServiceCollection, Action<DevelopmentActorOptions>?)` | DI extension | Scoped `IActorProvider` → `DevelopmentActorProvider`; reads `X-Test-Actor` JSON header; throws outside `IsDevelopment()`. |
| `AddCachingActorProvider<T>(this IServiceCollection)` where `T : class, IActorProvider` | DI extension | Wraps `T` in `CachingActorProvider` so a single resolution task is shared per request. |
| `ClaimsActorOptions` | Options | `ActorIdClaim` (default `"sub"`), `PermissionsClaim` (default `"permissions"`). `Claim.Type` literal match, plus a bidirectional fallback through the provider's curated short↔long mapping table. |
| `EntraActorOptions` | Options | `IdClaimType`, `MapPermissions`, `MapForbiddenPermissions`, `MapAttributes`. |
| `DevelopmentActorOptions` | Options | `DefaultActorId`, `DefaultPermissions`, `ThrowOnMalformedHeader`. |
| `ActorAttributes` (in `Trellis.Authorization`) | Constants | Well-known attribute keys: `TenantId`, `PreferredUsername`, `AuthorizedParty`, `AuthorizedPartyAcr`, `AuthContextClassReference`, `IpAddress`, `MfaAuthenticated`. |

Full signatures: [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md).

## Installation

```bash
dotnet add package Trellis.Asp
```

The actor providers ship in `Trellis.Asp` under namespace `Trellis.Asp.Authorization`. Domain primitives (`Actor`, `IActorProvider`, `ActorAttributes`) come from `Trellis.Authorization`.

## Quick start

Validate any OIDC token (Auth0, Google, Okta, custom) with the standard ASP.NET Core `JwtBearer` middleware, register `ClaimsActorProvider`, and read the resolved `Actor` from a protected endpoint. The example below uses Auth0; substitute your own `Authority` and `Audience`.

```csharp
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;
using Trellis.Authorization;

var builder = WebApplication.CreateBuilder(args);
var auth = builder.Configuration.GetSection("Authentication");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = auth["Authority"];   // e.g. https://your-tenant.auth0.com/
        options.Audience  = auth["Audience"];    // your API audience
        // Recommended: keep claim names round-tripping with RFC 7519 / OIDC short forms.
        // ASP.NET Core's default of `true` silently remaps short claim names onto WS-* URNs
        // before they reach the actor provider — set false unless you know you need it.
        options.MapInboundClaims = false;
    });

builder.Services.AddAuthorization();
builder.Services.AddClaimsActorProvider(options =>
{
    options.ActorIdClaim     = "sub";
    options.PermissionsClaim = "permissions";
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/me", [Authorize] async (IActorProvider actorProvider, CancellationToken ct) =>
{
    var maybeActor = await actorProvider.GetCurrentActorAsync(ct);
    if (!maybeActor.TryGetValue(out var actor))
        return Results.Unauthorized();

    return Results.Ok(new
    {
        actor.Id,
        Permissions = actor.Permissions.OrderBy(p => p).ToArray(),
    });
});

app.Run();
```

Matching `appsettings.json`:

```json
{
  "Authentication": {
    "Authority": "https://your-tenant.auth0.com/",
    "Audience":  "https://api.example.com"
  }
}
```

> [!NOTE]
> The actor provider extracts an `Actor` from `HttpContext.User`. It does not validate tokens — keep `AddJwtBearer` (or any other authentication scheme) in front of it. For the framework's failure-mode contract (when `Maybe<Actor>.None` becomes 401 vs when a configuration bug becomes 500), see [ASP.NET Core Authorization → Mental model](integration-asp-authorization.md#mental-model).

> [!TIP]
> Microsoft Entra ID consumers can substitute `AddEntraActorProvider()` for `AddClaimsActorProvider(...)` to opt in to Entra-specific defaults (App Roles, ABAC attributes from `tid`/`oid`/`amr`, multi-tenant). See [Microsoft (Entra ID)](#microsoft-entra-id) below.

## Provider recipes

Pick one provider per environment. The choice is driven by token shape, not by sign-in protocol.

| Provider | Use when |
|---|---|
| `ClaimsActorProvider` | Token has a flat actor-id claim and a flat permissions claim (most OIDC issuers — Auth0, Okta, Google, custom). |
| `EntraActorProvider` | Issuer is Microsoft Entra ID v2.0 and you want `oid` / `roles` / `tid` / `amr` mapped out of the box. |
| Subclass of `ClaimsActorProvider` | Token has nested or computed claims (Keycloak `realm_access.roles`, claims merged from a database). |
| `DevelopmentActorProvider` | Local development or integration tests — `IsDevelopment()` only. |

### Generic OIDC

For any standards-compliant OIDC issuer — Auth0, Okta, Google, or a custom IdP — register `ClaimsActorProvider` and name the two claim types. The Quick start above shows the canonical shape.

| Provider | Typical `ActorIdClaim` | Typical `PermissionsClaim` |
|---|---|---|
| Auth0 (RBAC) | `sub` | `permissions` |
| Okta (custom claim) | `sub` | `permissions` (or `scp` — see [Scope and permission extraction](#scope-and-permission-extraction)) |
| Google sign-in | `sub` | none — token only proves identity; load app permissions from your own store |
| Custom OIDC | `sub` | whatever your IdP emits |

If the token only proves identity (Google is the canonical case), `Actor.Permissions` will be empty. That is a valid starting point: gate endpoints with `[Authorize]` for "signed in" and load fine-grained permissions from your database via a custom `IActorProvider` — see [Database-backed permissions](integration-db-permissions.md) for the wrapping pattern.

### Microsoft (Entra ID)

Microsoft Entra ID is supported as a first-class specialization with extra defaults that cover the standard Entra claim set out of the box. **Use it instead of generic OIDC when** your tokens come from Entra and you want any of:

- **App Roles** — Entra's `roles` claim (one claim per role) flattened automatically into `Actor.Permissions`.
- **ABAC attributes** — `tid` (tenant id), `oid` (Entra object id), `preferred_username`, `azp`, `azpacr`, `acrs`, `mfa` from `amr`, and request `ip_address` populated automatically into `Actor.Attributes`.
- **Long-form `oid` fallback** — when `IdClaimType` is the default `http://schemas.microsoft.com/identity/claims/objectidentifier`, the provider falls back to the short `"oid"` claim.
- **Multi-tenant pinning** — Entra-specific issuer-validator pattern (see [Multi-tenant Entra](#multi-tenant-entra)).

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

var auth = configuration.GetSection("Authentication");

services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = auth["Authority"];   // https://login.microsoftonline.com/<tenant-id>/v2.0
        options.Audience  = auth["Audience"];    // api client id (no api:// prefix for v2.0 audiences)
        options.MapInboundClaims = false;        // recommended; see Quick start
    });

services.AddEntraActorProvider();
```

`EntraActorProvider` derives from `ClaimsActorProvider` and overrides `GetCurrentActorAsync` to apply the Entra-specific delegates. See `EntraActorOptions` defaults in [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md#entraactoroptions) and [ASP.NET Core Authorization → Customizing claim mapping](integration-asp-authorization.md#customizing-claim-mapping) for the override delegates (`MapPermissions`, `MapForbiddenPermissions`, `MapAttributes`).

### Google sign-in

Google issues OIDC ID tokens that prove identity but carry no application permissions. Validate them with `ClaimsActorProvider` and supply permissions from your own store. Google's `iss` claim has two valid forms (`https://accounts.google.com` and `accounts.google.com`); accept both.

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://accounts.google.com";
        options.Audience  = configuration["Authentication:Audience"]; // your OAuth client id
        options.TokenValidationParameters.ValidIssuers =
        [
            "https://accounts.google.com",
            "accounts.google.com",
        ];
        options.MapInboundClaims = false;
    });

services.AddClaimsActorProvider(options =>
{
    options.ActorIdClaim = "sub";
    // Google does not emit application permissions in the ID token. Use a database-backed
    // permission store (see Database-Backed Permissions article) or synthesize via OnTokenValidated.
});
```

### Sign in with Apple (iOS / macOS app)

Apple issues OIDC ID tokens through the "Sign in with Apple" flow. A native iOS or macOS app obtains the ID token via `ASAuthorizationAppleIDProvider` and sends it to your Trellis backend as a bearer token; the backend validates it like any other OIDC token. Apple's tokens only prove identity — they carry no application permissions, so load those from your own store (same pattern as Google).

**Apple-specific token facts**

| Field | Value |
|---|---|
| Issuer (`iss`) | `https://appleid.apple.com` |
| Audience (`aud`) | The app's **bundle identifier** for a native iOS/macOS app (e.g. `com.example.MyApp`); the **Services ID** for web sign-in. |
| Subject (`sub`) | A stable, team-scoped opaque user identifier. The same Apple account yields different `sub` values across different Apple developer teams. |
| `email` | May be present once at first sign-in. May be a **private relay address** (`*@privaterelay.appleid.com`) — treat as opaque, do not use as a primary key. |
| Name | **Not in the token.** Apple returns the user's full name to the app in the authorization response **only on the first sign-in**; the app must forward it to the backend and the backend must persist it. |
| Token lifetime | ~10 minutes. Apps refresh via Apple's `/auth/token` endpoint (refresh tokens are long-lived). |

#### Backend wiring

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://appleid.apple.com";
        // For a native iOS/macOS app, this is the app's bundle id.
        // For web (Sign in with Apple JS), this is the Services ID configured in
        // Apple Developer → Certificates, Identifiers & Profiles.
        options.Audience  = configuration["Authentication:Audience"]; // e.g. com.example.MyApp
        options.TokenValidationParameters.ValidIssuer = "https://appleid.apple.com";
        options.MapInboundClaims = false;
    });

services.AddClaimsActorProvider(options =>
{
    options.ActorIdClaim = "sub";
    // Apple does not emit application permissions. Load them from your own store
    // via a database-backed IActorProvider (see the Database-Backed Permissions article)
    // or synthesize via JwtBearerEvents.OnTokenValidated.
});
```

If you support **both** an iOS app (bundle id audience) and a web sign-in (Services ID audience) on the same backend, set `TokenValidationParameters.ValidAudiences` instead of `JwtBearerOptions.Audience`:

```csharp
options.TokenValidationParameters.ValidAudiences =
[
    "com.example.MyApp",            // iOS bundle id
    "com.example.MyApp.SignInWithApple", // Services ID for web
];
```

#### iOS client flow

The iOS app obtains an ID token from Apple, then calls your API with it as a bearer token:

```swift
import AuthenticationServices

final class SignInDelegate: NSObject, ASAuthorizationControllerDelegate {
    func authorizationController(
        controller: ASAuthorizationController,
        didCompleteWithAuthorization authorization: ASAuthorization
    ) {
        guard
            let credential = authorization.credential as? ASAuthorizationAppleIDCredential,
            let tokenData  = credential.identityToken,
            let idToken    = String(data: tokenData, encoding: .utf8)
        else { return }

        // First sign-in only: credential.fullName / credential.email are populated.
        // Forward them to your backend so it can persist them; subsequent sign-ins
        // will return nil for both.
        Task { try await callTrellisApi(idToken: idToken,
                                        fullName: credential.fullName,
                                        email: credential.email) }
    }
}

func callTrellisApi(idToken: String, fullName: PersonNameComponents?, email: String?) async throws {
    var request = URLRequest(url: URL(string: "https://api.example.com/me")!)
    request.setValue("Bearer \(idToken)", forHTTPHeaderField: "Authorization")
    _ = try await URLSession.shared.data(for: request)
}
```

#### Operational notes

- **Capture user details on the first sign-in.** Apple only returns the user's full name and (real) email address in the *authorization response*, not in the ID token, and only on the first sign-in. If the app doesn't forward them to the backend that first time, they're gone — Apple will not resurface them. Persist them server-side keyed by `sub`.
- **Private relay emails.** When the user chooses "Hide My Email", `email` is a routable `*@privaterelay.appleid.com` address. It is stable for that user/app pair. Don't treat it as a verified contact for spam-sensitive purposes without re-confirming.
- **`sub` is team-scoped.** If you publish multiple apps under different Apple teams, the same human will have different `sub` values. Don't use `sub` as a cross-app primary key unless all apps share a team.
- **Server-to-server validation only.** Don't trust the client's claim of "I signed in as X" — the backend must always validate the ID token signature against Apple's JWKS (`AddJwtBearer` does this automatically via OIDC discovery at `https://appleid.apple.com/.well-known/openid-configuration`).
- **No app roles or scopes.** Treat Sign in with Apple as identity-only and layer your own permissions on top, e.g. via a custom `IActorProvider` that decorates `ClaimsActorProvider` and merges permissions from your database.

### Android app

An Android app authenticates the user against an OIDC provider on the device, obtains a bearer token (an OIDC ID token from Google's Credential Manager / Apple, or an OAuth 2.0 access token from MSAL; AppAuth can return either), and sends it to the Trellis backend as `Authorization: Bearer <token>`. The **backend wiring is identical to the IdP-specific recipe above** — the choice of `Authority` and `Audience` is dictated by which IdP the app signed in with (Google, Microsoft Entra, Auth0, your own OIDC server) and by which token type that flow produces. What changes on Android is the client-side library and a few audience-naming quirks.

| IdP from the Android app | Recommended client library | Backend recipe |
|---|---|---|
| Google sign-in | [Credential Manager](https://developer.android.com/identity/sign-in/credential-manager-siwg) (`androidx.credentials`) with `GetGoogleIdOption` | [Google sign-in](#google-sign-in) |
| Microsoft Entra ID (work / school / personal MS accounts) | [MSAL for Android](https://learn.microsoft.com/entra/identity-platform/msal-android) (`com.microsoft.identity.client:msal`) | [Microsoft (Entra ID)](#microsoft-entra-id) |
| Auth0 | [Auth0 Android SDK](https://auth0.com/docs/quickstart/native/android) | [Generic OIDC](#generic-oidc) (Auth0 row) |
| Okta | [Okta OIDC Android](https://github.com/okta/okta-oidc-android) (or AppAuth) | [Generic OIDC](#generic-oidc) (Okta row) |
| Custom / any standards-compliant OIDC | [AppAuth for Android](https://github.com/openid/AppAuth-Android) | [Generic OIDC](#generic-oidc) |

> [!NOTE]
> The legacy "Google Sign-In for Android" SDK (`com.google.android.gms:play-services-auth`) is deprecated. New apps use Credential Manager. Existing apps on the old SDK still produce a valid OIDC ID token that the backend validates the same way.

#### Google sign-in from Android (Credential Manager)

Google sign-in from Android has one quirk the backend recipe doesn't mention: the audience claim. When the Android app calls Credential Manager with `setServerClientId(...)`, it passes the **Web OAuth client ID** configured in Google Cloud Console — not the Android OAuth client ID. The ID token Google issues then carries that Web client ID as its `aud` claim. The Trellis backend must validate against the same value.

Google Cloud Console setup:

1. Create an **Android** OAuth client (package name + SHA-1 of your signing key). This client has no client secret. It's what tells Google which app is calling.
2. Create a **Web application** OAuth client. Its client id is what you pass as `serverClientId` and what the backend validates as `Audience`. The "Web" name is misleading — for native-app server-side validation flows, Google still requires a Web-type client.

Android client (Kotlin):

```kotlin
import android.content.Context
import androidx.credentials.CredentialManager
import androidx.credentials.GetCredentialRequest
import com.google.android.libraries.identity.googleid.GetGoogleIdOption
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential
import okhttp3.OkHttpClient
import okhttp3.Request

suspend fun signInAndCallApi(context: Context) {
    val googleIdOption = GetGoogleIdOption.Builder()
        // The WEB OAuth client id from Google Cloud Console. NOT the Android client id.
        // The ID token's `aud` claim will equal this value.
        .setServerClientId("YOUR_WEB_CLIENT_ID.apps.googleusercontent.com")
        .setFilterByAuthorizedAccounts(false)
        .build()

    val request = GetCredentialRequest.Builder()
        .addCredentialOption(googleIdOption)
        .build()

    val response = CredentialManager.create(context).getCredential(context, request)
    val credential = GoogleIdTokenCredential.createFrom(response.credential.data)
    val idToken = credential.idToken

    val http = OkHttpClient()
    val apiRequest = Request.Builder()
        .url("https://api.example.com/me")
        .header("Authorization", "Bearer $idToken")
        .build()
    http.newCall(apiRequest).execute().use { /* read response */ }
}
```

Backend (`Program.cs`) — same as [Google sign-in](#google-sign-in); make sure `Audience` matches the **Web** client id:

```csharp
services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://accounts.google.com";
        options.Audience  = "YOUR_WEB_CLIENT_ID.apps.googleusercontent.com";
        options.TokenValidationParameters.ValidIssuers =
        [
            "https://accounts.google.com",
            "accounts.google.com",
        ];
        options.MapInboundClaims = false;
    });

services.AddClaimsActorProvider(options => options.ActorIdClaim = "sub");
```

#### Microsoft Entra ID from Android (MSAL)

MSAL Android acquires an access token for your API's resource scope. The `aud` claim on the resulting JWT is your API client id (the same value the backend validates as `Audience`).

Android client (Kotlin):

```kotlin
import com.microsoft.identity.client.IPublicClientApplication
import com.microsoft.identity.client.PublicClientApplication
import com.microsoft.identity.client.SilentAuthenticationCallback

val scopes = arrayOf("api://YOUR_API_CLIENT_ID/.default")

PublicClientApplication.createMultipleAccountPublicClientApplication(
    context,
    R.raw.msal_config, // JSON config with client_id, redirect_uri, authorities
    object : IPublicClientApplication.IMultipleAccountApplicationCreatedListener {
        override fun onCreated(app: com.microsoft.identity.client.IMultipleAccountPublicClientApplication) {
            // ... acquireToken(activity, scopes, callback) — see MSAL docs for the full flow.
            // The resulting authResult.accessToken is what you send as Authorization: Bearer.
        }
        override fun onError(exception: com.microsoft.identity.client.exception.MsalException) { /* log */ }
    },
)
```

Backend wiring is identical to [Microsoft (Entra ID)](#microsoft-entra-id). The `msal_config` JSON resource must specify your Entra app's `client_id`, `redirect_uri` (`msauth://<package>/<base64-sha1>`), and the authority (`https://login.microsoftonline.com/<tenant-id>`).

#### Generic OIDC from Android (AppAuth)

[AppAuth for Android](https://github.com/openid/AppAuth-Android) handles the OIDC authorization-code-with-PKCE flow against any standards-compliant issuer (Auth0, Okta, Keycloak, your own IdP). The resulting `AuthState.accessToken` (or `idToken`, depending on which claim your backend validates against) is what gets sent as `Authorization: Bearer …`. Backend wiring matches the corresponding [Generic OIDC](#generic-oidc) row.

#### Operational notes

- **Token type.** Google Credential Manager gives an **ID token** (audience = Web OAuth client id). MSAL gives an **access token** (audience = your API resource). Both arrive at the backend as `Authorization: Bearer …`; the JWT handler validates whichever the `Audience` configuration matches. Don't mix them: an access token sent where the backend expects an ID token (or vice-versa) fails audience validation with HTTP 401.
- **App identity vs user identity.** The Android OAuth client (Google) or Entra Android client (MSAL) authenticates the *app* to the IdP; the user signs in to the IdP through that app. The token your backend receives identifies the *user*. There's no Trellis-side concept of "this token came from Android" — the backend can't (and shouldn't) distinguish device platforms from a validated token.
- **HTTPS + certificate pinning.** All API calls from the app must be HTTPS. Android 7+ ignores user-installed CAs for app traffic by default; this is correct for production. If you need certificate pinning, configure it in `network_security_config.xml`.
- **Refresh tokens stay on the device.** The backend never sees a refresh token. The app refreshes the access/ID token via the IdP (Credential Manager / MSAL / AppAuth handle this) and sends the fresh bearer token on each call.
- **No `X-Test-Actor` from device builds.** [Development defaults](#development-defaults) is for local backend dev only. Don't ship a debug build that sends `X-Test-Actor` — the provider fails closed outside `IsDevelopment()` on the backend, but the convention is to not depend on it from a real device at all.
- **Permissions still come from your store.** Google / Apple / generic OIDC tokens carry identity only. Layer Trellis permissions on top via a database-backed `IActorProvider` regardless of which Android sign-in flow the app uses. Entra tokens with `roles` are the exception (App Roles flatten into `Actor.Permissions` via the default Entra mapping).

### Facebook (via OIDC bridge)

Facebook's Login API is OAuth 2.0 (not OIDC). Use an OIDC bridge — Auth0, Microsoft Entra External Identities, Amazon Cognito, or your own IdentityServer — to issue OIDC tokens after Facebook authentication. The bridge's tokens look like any other OIDC token to Trellis:

```csharp
services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = configuration["Authentication:BridgeAuthority"]; // your OIDC bridge
        options.Audience  = configuration["Authentication:Audience"];
        options.MapInboundClaims = false;
    });

services.AddClaimsActorProvider(options =>
{
    options.ActorIdClaim     = "sub";
    options.PermissionsClaim = "permissions"; // however your bridge surfaces app permissions
});
```

Bridge-specific configuration (Facebook app id / secret, redirect URIs, identity-provider linking) lives in the bridge's admin console, not in Trellis.

### Keycloak / nested claims

`ClaimsActorOptions` matches `Claim.Type` literally first, then falls back through the provider's curated short↔long mapping table (e.g. `"sub"` ↔ `ClaimTypes.NameIdentifier`, `"role"`/`"roles"` ↔ `ClaimTypes.Role`) — no JSON-path traversal. When a token contains a nested object (e.g. Keycloak's `{ "realm_access": { "roles": [...] } }`), the JWT handler stores the value as a raw JSON string under claim type `"realm_access"`, which is not in the mapping table. Subclass `ClaimsActorProvider` to project it.

```csharp
using System;
using System.Collections.Frozen;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis;
using Trellis.Asp.Authorization;
using Trellis.Authorization;

internal sealed record RealmAccess([property: JsonPropertyName("roles")] string[] Roles);

[JsonSerializable(typeof(RealmAccess))]
internal partial class KeycloakJsonContext : JsonSerializerContext { }

public sealed class KeycloakActorProvider(
    IHttpContextAccessor accessor,
    IOptions<ClaimsActorOptions> options) : ClaimsActorProvider(accessor, options)
{
    public override Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // No HttpContext is a configuration bug → throw → HTTP 500.
        var httpContext = HttpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        // No authenticated identity / missing id claim → no usable actor → Maybe.None,
        // which the mediator pipeline maps to Error.AuthenticationRequired (HTTP 401).
        var identity = httpContext.User.Identities
            .FirstOrDefault(i => i.IsAuthenticated) as ClaimsIdentity;
        if (identity is null)
            return Task.FromResult(Maybe<Actor>.None);

        var sub = identity.FindFirst(Options.ActorIdClaim)?.Value;
        if (sub is null)
            return Task.FromResult(Maybe<Actor>.None);

        var raw = identity.FindFirst("realm_access")?.Value;
        var roles = raw is null
            ? FrozenSet<string>.Empty
            : (JsonSerializer.Deserialize(raw, KeycloakJsonContext.Default.RealmAccess)?.Roles
                ?? Array.Empty<string>()).ToFrozenSet();

        return Task.FromResult(Maybe.From(Actor.Create(sub, roles)));
    }
}

// Register IOptions<ClaimsActorOptions> first (the subclass shares the base
// options type), then wrap with the caching helper.
services.AddClaimsActorProvider(opts => opts.ActorIdClaim = "sub");
services.AddCachingActorProvider<KeycloakActorProvider>();
```

`AddClaimsActorProvider(...)` configures `IOptions<ClaimsActorOptions>` (which `KeycloakActorProvider` consumes through its base constructor). `AddCachingActorProvider<T>` then registers `T` as scoped via `TryAddScoped` and wraps it with `CachingActorProvider`, so the JSON parse runs once per request even if multiple handlers ask for the actor. Because every `AddXxxActorProvider` Replaces the `IActorProvider` slot, the order matters: register the inner-provider's options helper first, then wrap.

## Multiple IdPs in one API

A single Trellis service can validate tokens from multiple IdPs at once — a typical "web frontend with Microsoft *and* Google sign-in buttons, plus iOS / Android apps signing in with Apple or Google" deployment. This is plain ASP.NET Core multi-scheme authentication: register one `AddJwtBearer("<scheme>", ...)` per issuer and list every scheme in the authorization policy so each is invoked per request.

### Register one scheme per IdP

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

builder.Services
    // No default scheme — the policy below names every accepted scheme explicitly.
    .AddAuthentication()
    .AddJwtBearer("Microsoft", o =>
    {
        o.Authority = "https://login.microsoftonline.com/<tenant-id>/v2.0";
        o.Audience  = "<entra-api-client-id>";
        o.MapInboundClaims = false;
    })
    .AddJwtBearer("Google", o =>
    {
        o.Authority = "https://accounts.google.com";
        // The Web OAuth client id from Google Cloud Console — also what Android
        // Credential Manager passes as setServerClientId.
        o.Audience  = "<google-web-oauth-client-id>";
        o.TokenValidationParameters.ValidIssuers =
        [
            "https://accounts.google.com",
            "accounts.google.com",
        ];
        o.MapInboundClaims = false;
    })
    .AddJwtBearer("Apple", o =>
    {
        o.Authority = "https://appleid.apple.com";
        // Bundle id for the iOS / macOS app; Services ID for web sign-in.
        // Use ValidAudiences if you accept both.
        o.Audience  = "<ios-bundle-id>";
        o.MapInboundClaims = false;
    });

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddAuthenticationSchemes("Microsoft", "Google", "Apple")
        .Build();
});
```

For each request, ASP.NET Core's `PolicyEvaluator` invokes every scheme listed in the policy and merges the successful principals into `HttpContext.User`. With a single bearer token, the non-matching `JwtBearerHandler`s return `AuthenticateResult.Fail(...)` (token signature / issuer / audience validation failed against that scheme's configuration); `PolicyEvaluator` ignores those failures and the policy succeeds as long as one scheme produced a principal. The downstream actor provider sees one validated principal regardless of which scheme matched.

> [!NOTE]
> If your authorization policy does **not** list every scheme, only the default scheme runs and tokens from the other IdPs come back 401. The pattern above sidesteps that by enumerating all schemes in the policy. As an alternative, a [forwarding scheme](https://learn.microsoft.com/aspnet/core/security/authentication/policyschemes) (`AddPolicyScheme(...)` with a `ForwardDefaultSelector` that picks per-request based on token `iss`) lets you keep a single default scheme.

### Pick an actor provider strategy

Different IdPs put the user id in different claims:

| IdP | Default user-id claim |
|---|---|
| Microsoft Entra ID v2.0 | `oid` (with `sub` also present, but `sub` is per-(user, audience) — not stable across audiences) |
| Google | `sub` |
| Apple | `sub` (team-scoped) |
| Auth0 / Okta / custom OIDC | `sub` |

Two viable strategies for a multi-IdP API:

**Strategy 1 — One provider, normalize on `sub`.** Register `AddClaimsActorProvider(o => o.ActorIdClaim = "sub")` only. Entra v2.0 tokens carry `sub` alongside `oid`, so the same provider works for every IdP listed above. The trade-off: Entra `sub` is **per-audience**, so two Entra-fronted APIs see different `sub` values for the same human; that is fine if you have one API or if you key users by `(iss, sub)`, and wrong if you need a single canonical user id across multiple Entra audiences.

**Strategy 2 — Route by issuer.** Wrap a thin router that inspects `HttpContext.User.FindFirstValue("iss")` and delegates to `EntraActorProvider` for Microsoft tokens (so you keep App Roles, ABAC attributes from `tid` / `oid` / `amr`, and the `oid` ↔ long-form fallback) and to `ClaimsActorProvider` for everything else.

```csharp
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trellis;
using Trellis.Asp.Authorization;
using Trellis.Authorization;

public sealed class IssuerRoutingActorProvider(
    IHttpContextAccessor accessor,
    EntraActorProvider entra,
    ClaimsActorProvider generic) : IActorProvider, IProvideActorVaryHeaders
{
    public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        var http = accessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var iss = http.User.FindFirstValue("iss");
        return iss?.StartsWith("https://login.microsoftonline.com/", StringComparison.Ordinal) == true
            ? entra.GetCurrentActorAsync(cancellationToken)
            : generic.GetCurrentActorAsync(cancellationToken);
    }

    // CachingActorProvider only delegates IProvideActorVaryHeaders to its directly wrapped
    // inner. Without this, VaryForActor() output-cache partitioning fails closed because
    // the wrapped router exposes no vary headers. Every Trellis-supported bearer-token IdP
    // partitions on the same header.
    public IReadOnlyCollection<string> VaryByHeaders { get; } = ["Authorization"];
}

// Register both inner providers as concrete types, then expose the router as IActorProvider.
builder.Services.AddHttpContextAccessor();
builder.Services.AddOptions<EntraActorOptions>();
builder.Services.AddOptions<ClaimsActorOptions>().Configure(o => o.ActorIdClaim = "sub");
builder.Services.TryAddScoped<EntraActorProvider>();
builder.Services.TryAddScoped<ClaimsActorProvider>();
builder.Services.AddCachingActorProvider<IssuerRoutingActorProvider>();
```

`AddCachingActorProvider<T>` wraps the router so the issuer lookup + downstream provider call runs once per request even if multiple handlers ask for the `Actor`.

### Permissions across IdPs

Application permissions are an **API-side concept**, not a token-side one. Each IdP carries them differently:

| IdP | Where permissions come from |
|---|---|
| Microsoft Entra ID | Entra App Roles → `roles` claim → flattened by `EntraActorOptions.MapPermissions` |
| Google / Apple | Not in the token; load from your own permission store via a custom `IActorProvider` |
| Auth0 / Okta | Either `permissions` (RBAC) or `scp` (delegated scopes); see [Scope and permission extraction](#scope-and-permission-extraction) |

For a multi-IdP API the simplest pattern is to derive a stable user key from `(iss, sub)` (or `(iss, oid)` for Entra), look up the user's permissions in your database, and merge them into `Actor.Permissions` — see [SSO → Synthesizing app permissions during token validation](#synthesizing-app-permissions-during-token-validation) for the `OnTokenValidated` hook that runs once per request, and [Database-Backed Permissions](integration-db-permissions.md) for the IActorProvider-wrapping pattern.

### Operational notes

- **One audience per scheme.** Each `AddJwtBearer("<scheme>", ...)` validates exactly one (or via `ValidAudiences`, a set of) audience values. A token whose `aud` matches no scheme returns HTTP 401.
- **Same scope, different schemes.** All registered schemes share `HttpContext.User` — once a token validates, the downstream actor provider, mediator pipeline, and handlers don't know (and don't need to know) which IdP issued it.
- **WWW-Authenticate.** Trellis's [`ResponseFailureWriter`](integration-asp-authorization.md#anatomy-of-a-401) synthesizes a single `WWW-Authenticate` header from the configured *default* challenge / authenticate scheme — it does not enumerate every registered scheme. With the parameterless `AddAuthentication()` shown above there is no default, so the synthesizer emits nothing. If you need clients to discover a specific Bearer challenge, either (a) set a default scheme on `AddAuthentication("<scheme>")` so synthesis fires, accepting that only that scheme's name is advertised, or (b) return `new Error.AuthenticationRequired("Bearer realm=\"api\"")` from the handler / failure mapper so `ResponseFailureWriter` emits it verbatim. If you need to advertise multiple challenges, write `WWW-Authenticate` yourself before Trellis renders the failure.
- **Diagnostics.** Log `iss` and the winning scheme on first request from each IdP — silent "wrong scheme picked" is almost always an audience mismatch.

## JWT validation options

Trellis does not own JWT validation — that lives in `Microsoft.AspNetCore.Authentication.JwtBearer`. The fields `JwtBearerHandler` validates flow into Trellis as follows:

| `JwtBearerOptions` setting | Effect on Trellis |
|---|---|
| `Authority` | Determines OIDC discovery / signing keys. If validation fails, `HttpContext.User` is unauthenticated and the actor provider returns `Maybe<Actor>.None`, which the mediator pipeline maps to `Error.AuthenticationRequired` (HTTP 401). |
| `Audience` (or `TokenValidationParameters.ValidAudiences`) | Must match the token `aud`. Mismatched audiences never reach the actor provider — the request is rejected with `401`. |
| `TokenValidationParameters.ValidIssuers` | Required when `Authority` does not match the literal `iss` claim (Google emits both `https://accounts.google.com` and `accounts.google.com`). |
| `TokenValidationParameters.ValidateIssuer = false` | Multi-tenant Entra requires this; tenant pinning then lives in `MapAttributes` or a custom validator. See [Multi-tenant Entra](#multi-tenant-entra). |
| `Events.OnTokenValidated` | The hook for synthesizing extra `Claim` instances before Trellis sees them — useful when your IdP issues identity but your app issues permissions. |

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://accounts.google.com";
        options.Audience  = configuration["Authentication:Audience"];
        options.TokenValidationParameters.ValidIssuers =
        [
            "https://accounts.google.com",
            "accounts.google.com",
        ];
    });
```

## Claim mapping

Each provider exposes a small surface of mapping options. The full default tables (claim names, attribute keys, fall-back rules) live in [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md#entraactoroptions); this section covers the everyday overrides.

### Synthesizing app permissions during token validation

When the IdP only issues identity, add permission claims in `OnTokenValidated`. `ClaimsActorProvider` aggregates every `Claim` whose `Type` matches `PermissionsClaim` (or its counterpart in the provider's curated short↔long mapping table) via `FindAll`, so multiple `Claim` instances of the same type — and matches across both forms — are flattened into a deduplicated set automatically.

```csharp
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = auth["Authority"];
        options.Audience  = auth["Audience"];
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is not ClaimsIdentity identity)
                    return Task.CompletedTask;

                identity.AddClaim(new Claim("permissions", "todos:read"));

                var email = identity.FindFirst("email")?.Value;
                if (!string.IsNullOrWhiteSpace(email)
                    && email.EndsWith("@yourcompany.com", StringComparison.OrdinalIgnoreCase))
                {
                    identity.AddClaim(new Claim("permissions", "todos:create"));
                }

                return Task.CompletedTask;
            },
        };
    });

services.AddClaimsActorProvider(options =>
{
    options.ActorIdClaim     = "sub";
    options.PermissionsClaim = "permissions";
});
```

> [!NOTE]
> `ClaimsActorProvider` only reads two claim types and never writes to `Actor.Attributes` or `Actor.ForbiddenPermissions`. Anything richer — ABAC attributes, deny lists, or computed permissions — belongs on `EntraActorProvider` (via `MapAttributes` / `MapForbiddenPermissions`) or a subclass. The full ABAC story is in [ASP.NET Core Authorization → Customizing claim mapping](integration-asp-authorization.md#customizing-claim-mapping).

### Customizing the Entra mapping

`EntraActorOptions` has three independent delegates and one identifier knob.

| Member | Default | Override to... |
|---|---|---|
| `IdClaimType` | long `objectidentifier` URI (falls back to short `"oid"`) | Use `sub`, an employee ID, or a custom claim. |
| `MapPermissions` | union of `roles` and `ClaimTypes.Role` | Flatten Entra app roles into fine-grained permissions; merge DB-sourced grants. |
| `MapForbiddenPermissions` | empty `HashSet<string>` | Project a deny-list claim. |
| `MapAttributes` | `tid`, `preferred_username`, `azp`, `azpacr`, `acrs` from claims; `ip_address` from connection; `mfa` from any `amr == "mfa"` | Add tenant-scoped or request-scoped attributes. |

> [!WARNING]
> Any exception thrown from `MapPermissions`, `MapForbiddenPermissions`, or `MapAttributes` is rewrapped by `EntraActorProvider` as `InvalidOperationException("EntraActorOptions.<delegate> threw an exception while mapping the authenticated user's claims.")`. The provider never silently defaults.

## Scope and permission extraction

Two common shapes for "what is this token allowed to do":

**App roles (Entra application permissions).** Each role is a separate claim of type `roles`. The default `MapPermissions` already collects them.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

services.AddEntraActorProvider(options =>
{
    var rolePermissionMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Catalog.Admin"]  = ["products:read", "products:write", "products:delete"],
        ["Catalog.Reader"] = ["products:read"],
    };

    options.MapPermissions = claims => claims
        .Where(c => string.Equals(c.Type, "roles", StringComparison.OrdinalIgnoreCase))
        .SelectMany(c => rolePermissionMap.TryGetValue(c.Value, out var perms) ? perms : Array.Empty<string>())
        .ToHashSet(StringComparer.Ordinal);
});
```

**Delegated scopes.** Entra (and many other OAuth servers) emit a single `scp` claim whose value is a space-separated string. Split it before flattening into permissions.

```csharp
services.AddEntraActorProvider(options =>
{
    options.MapPermissions = claims => claims
        .Where(c => string.Equals(c.Type, "scp", StringComparison.OrdinalIgnoreCase))
        .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .ToHashSet(StringComparer.Ordinal);
});
```

Trellis does not enforce a permission naming convention — pick one (`products:read` or `Products.Read`) and stay consistent. Scoped permissions use `Actor.PermissionScopeSeparator` (`':'`); `actor.HasPermission("products:read", tenantId)` checks for the joined string `products:read:<tenantId>`. See [`trellis-api-authorization.md → Actor`](../api_reference/trellis-api-authorization.md#actor).

## Multi-tenant Entra

A multi-tenant Entra app accepts tokens from any tenant the app is consented in. `Authority` becomes `https://login.microsoftonline.com/common/v2.0` (or `/organizations/v2.0`), the token's `iss` is per-tenant, and your API decides which tenants are allowed.

```csharp
using System;
using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Trellis.Asp.Authorization;

var allowedTenants = configuration.GetSection("Authentication:AllowedTenants").Get<string[]>()
    ?? Array.Empty<string>();

services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://login.microsoftonline.com/common/v2.0";
        options.Audience  = configuration["Authentication:Audience"];

        options.TokenValidationParameters.ValidateIssuer = false;
        options.TokenValidationParameters.IssuerValidator = (issuer, _, _) =>
            issuer.StartsWith("https://login.microsoftonline.com/", StringComparison.Ordinal)
                ? issuer
                : throw new SecurityTokenInvalidIssuerException($"Untrusted issuer '{issuer}'.");
    });

services.AddEntraActorProvider();
```

Then enforce the tenant allow-list inside handlers using the `tid` attribute the default `MapAttributes` already populates:

```csharp
using System.Linq;
using Trellis;
using Trellis.Authorization;

var tenantId = actor.GetAttribute(ActorAttributes.TenantId);

if (tenantId is null || !allowedTenants.Contains(tenantId, StringComparer.Ordinal))
    return Result.Fail(new Error.Forbidden("tenant.not-allowed") { Detail = $"Tenant '{tenantId}' is not provisioned." });
```

> [!TIP]
> Tenant-scoped permissions ride on the same `Actor.HasPermission(name, scope)` helper: `actor.HasPermission("documents:read", tenantId)` checks `documents:read:<tenantId>`.

## Development defaults

`AddDevelopmentActorProvider` reads an `X-Test-Actor` JSON header (case-insensitive property names) and falls back to a configurable default actor when the header is missing. It throws `InvalidOperationException` whenever `IHostEnvironment.IsDevelopment()` is `false` — including in Production with no header — which is the fail-closed safety net.

```csharp
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis.Asp.Authorization;

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDevelopmentActorProvider(options =>
    {
        options.DefaultActorId         = "developer@local";
        options.DefaultPermissions     = new HashSet<string> { "todos:read", "todos:create" };
    });
}
else
{
    builder.Services.AddEntraActorProvider();
}
```

Send a header from any HTTP client to impersonate a specific actor:

```powershell
$actor = '{"Id":"local-user","Permissions":["todos:read","todos:create"],"ForbiddenPermissions":[],"Attributes":{"tid":"local-tenant","mfa":"false"}}'
Invoke-RestMethod https://localhost:5001/me -Headers @{ "X-Test-Actor" = $actor }
```

For test clients, `WebApplicationFactoryExtensions.CreateClientWithActor(...)` writes the same header (see [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md)).

> [!WARNING]
> Use the **same SSO provider** in staging and production — only `Authority` and `Audience` should differ between environments. A staging path that swaps `EntraActorProvider` for `ClaimsActorProvider` (or vice versa) will hide token-format and claim-mapping bugs until they hit real users.

## Practical guidance

- **Never authorize on `preferred_username`.** It is a display claim and can change. Use `oid` (Entra) or `sub` (everyone else).
- **Use `ActorAttributes` constants.** `actor.GetAttribute(ActorAttributes.TenantId)` survives renames; `actor.GetAttribute("tid")` does not.
- **Keep one mapping site.** Flatten roles to permissions inside `MapPermissions` once, not in every handler.
- **Pick one naming convention for permissions.** `todos:read` and `Todos.Read` are different strings — `Actor.Permissions` is a `FrozenSet<string>` with ordinal comparison.
- **Wrap expensive providers** with `AddCachingActorProvider<T>()` so DB-backed permission lookups run once per request.
- **Keep `AddDevelopmentActorProvider` behind `IsDevelopment()`.** The provider also fails closed at runtime, but the registration discipline avoids accidental dependency on `X-Test-Actor` from staging integration tests.
- **Match the audience exactly.** The number-one cause of "token validates somewhere else but not here" is `Audience` not matching the token's `aud` claim.
- **Let mapper exceptions bubble.** A buggy `MapPermissions` becomes `InvalidOperationException("EntraActorOptions.MapPermissions threw ...")`; surfacing it during development reveals bad role tables faster than a silent fallback.

## Cross-references

- ASP DI surface (actor providers, options, response mapping): [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md)
- Domain primitives (`Actor`, `IActorProvider`, `IAuthorize`, `IAuthorizeResource<T>`, `ActorAttributes`): [`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md)
- `Result`, `Error.Forbidden`: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- Deeper authorization patterns (claim mapping, ABAC, mediator integration, resource loaders): [ASP.NET Core Authorization](integration-asp-authorization.md)
- End-to-end token-validation tests against real Entra: [Testing with Entra ID Tokens](integration-entra-testing.md)
- Test-client `X-Test-Actor` helper: [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md)
