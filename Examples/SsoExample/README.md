# SSO Example — Multi-Provider Authentication with Trellis

A minimal ASP.NET Core API demonstrating `ClaimsActorProvider` working with multiple identity providers. The sample is **configuration-driven** — swap providers by editing `appsettings.json`.

## Quick Start (Development — no provider needed)

```bash
dotnet run
```

In Development mode the app uses `DevelopmentActorProvider`, which reads identity from the `X-Test-Actor` header:

```bash
curl http://localhost:5050/api/me \
  -H 'X-Test-Actor: {"Id":"alice","Permissions":["orders:read","orders:create"],"Attributes":{"tid":"tenant-1"}}'
```

Response:

```json
{
  "id": "alice",
  "permissions": ["orders:read", "orders:create"],
  "attributes": { "tid": "tenant-1" }
}
```

Without the header, a default `"development"` actor is returned.

---

## Provider Configuration

### Microsoft Entra ID

1. Register an app at [entra.microsoft.com](https://entra.microsoft.com).
2. Update `appsettings.json`:

```json
{
  "Authentication": {
    "Authority": "https://login.microsoftonline.com/{tenant-id}/v2.0",
    "Audience": "{client-id}",
    "ActorIdClaim": "sub",
    "PermissionsClaim": "roles"
  }
}
```

For Entra-specific features (role-to-permission mapping, ABAC attributes, MFA detection), replace `AddClaimsActorProvider()` with `AddEntraActorProvider()` in `Program.cs`.

### Google

1. Register at [console.cloud.google.com](https://console.cloud.google.com).
2. Update `appsettings.json`:

```json
{
  "Authentication": {
    "Authority": "https://accounts.google.com",
    "Audience": "{client-id}.apps.googleusercontent.com",
    "ActorIdClaim": "sub",
    "PermissionsClaim": "permissions"
  }
}
```

### Auth0

1. Register at [auth0.com](https://auth0.com) dashboard.
2. Create an API in Auth0 Dashboard → APIs.
3. **Enable RBAC:** API Settings → RBAC → Enable, and check "Add Permissions in the Access Token".
4. Assign permissions to users via Auth0 roles.
5. Update `appsettings.json`:

```json
{
  "Authentication": {
    "Authority": "https://{your-domain}.auth0.com/",
    "Audience": "{api-identifier}",
    "ActorIdClaim": "sub",
    "PermissionsClaim": "permissions"
  }
}
```

> **Note:** Without enabling RBAC and "Add Permissions in the Access Token" in the Auth0 API settings, tokens will not contain the `permissions` claim.

### Generic OIDC

Any provider that issues standard JWTs works. Set `Authority` and `Audience` to match, and map `ActorIdClaim` / `PermissionsClaim` to the claim names your provider uses.

---

## Launch Profiles

| Profile | Port | Actor Provider | Use case |
|---------|------|---------------|----------|
| **Development** | `:5050` | `DevelopmentActorProvider` | Testing without real login — uses `X-Test-Actor` header |
| **Production** | `:5051` | `ClaimsActorProvider` | Testing with a real OIDC provider — requires valid JWT |

```bash
dotnet run --launch-profile Development   # X-Test-Actor header mode
dotnet run --launch-profile Production    # Real JWT required
```

---

## Getting a Token for Production Mode

### Microsoft Entra (Azure CLI)

```bash
az login
TOKEN=$(az account get-access-token --resource {client-id} --query accessToken -o tsv)
curl http://localhost:5051/api/me -H "Authorization: Bearer $TOKEN"
```

### Google (ID Token)

1. Go to https://developers.google.com/oauthplayground
2. Select the `openid` scope (and optionally `email`, `profile`)
3. Exchange the authorization code for tokens
4. Copy the **`id_token`** (not `access_token`) from the response — it's the JWT

```bash
curl http://localhost:5051/api/me -H "Authorization: Bearer {id-token}"
```

> **Note:** Google OAuth2 access tokens are opaque strings meant for calling Google APIs. ASP.NET's `JwtBearer` middleware cannot validate them. The `id_token` is a standard JWT containing `sub`, `email`, and other OIDC claims.

### Auth0 (Dashboard)

1. Go to Auth0 Dashboard → APIs → your API → Test tab
2. Copy the generated `access_token`
3. Use it:

```bash
curl http://localhost:5051/api/me -H "Authorization: Bearer {access-token}"
```

> **Note:** Without valid provider credentials in `appsettings.json`, the Production profile rejects all requests with `401 Unauthorized`.

---

## How It Works

| Environment | Actor Provider | Authentication | Identity Source |
|---|---|---|---|
| Development | `DevelopmentActorProvider` | None (anonymous allowed) | `X-Test-Actor` HTTP header |
| Production | `ClaimsActorProvider` | JWT bearer (required) | JWT claims from the configured OIDC provider |

In Development, JWT authentication is not registered — requests go straight to `DevelopmentActorProvider` which reads the `X-Test-Actor` header. In Production, a fallback authorization policy requires authenticated users on all endpoints, and `ClaimsActorProvider` maps JWT claims to an actor.

The `/api/me` endpoint returns the current actor's identity, permissions, and attributes — useful for verifying the authentication pipeline is working correctly.

## Project References

This example references the local Trellis packages via `ProjectReference` so it always builds against the latest source.
