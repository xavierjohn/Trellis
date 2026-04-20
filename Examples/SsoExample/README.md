# SSO Example

This example shows how to turn an authenticated principal into a Trellis `Actor` using either a development header or a real OIDC/JWT provider.

## What You'll Learn
- How `DevelopmentActorProvider` speeds up local testing
- How `ClaimsActorProvider` maps JWT claims to actor identity and permissions
- How to switch between development and production authentication paths

## Prerequisites
- .NET 10 SDK
- For `Production`, a configured OIDC provider and valid JWT

## Run It
```bash
dotnet run --launch-profile Development
dotnet run --launch-profile Production
```

## Endpoints
| Profile | URL | Notes |
|------|------|-------|
| `Development` | `http://localhost:5050/api/me` | Reads actor data from `X-Test-Actor` |
| `Production` | `http://localhost:5051/api/me` | Requires a bearer token |

Use [`api.http`](api.http) to exercise both profiles from VS Code REST Client
or Visual Studio's HTTP file support.

## Key Files
| File | What It Shows |
|------|--------------|
| `Program.cs` | Environment-specific auth and actor provider wiring |
| `Controllers/MeController.cs` | Endpoint that returns the current actor |
| `appsettings.json` | Authority, audience, and claim mapping |
| `Properties/launchSettings.json` | Development and production launch profiles |

## Related Docs
- [Setting Up Single Sign-On (SSO)](https://xavierjohn.github.io/Trellis/articles/integration-sso.html)
- [ASP.NET Core Authorization](https://xavierjohn.github.io/Trellis/articles/integration-asp-authorization.html)
