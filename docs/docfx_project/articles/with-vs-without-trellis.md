# With vs Without Trellis: AI Code Generation Study

**Level:** Case Study | **Time:** 10 min

Does a framework like Trellis actually improve AI-generated code, or does it just add complexity? We tested this empirically by having three AI models build the same Order Management service twice — once with Trellis, once without — and then cross-evaluated the results.

## Study Design

**Spec:** A realistic Order Management System with 3 aggregates (Customer, Product, Order), 16 API endpoints, a 6-state order lifecycle, role-based authorization with resource-level ownership checks, stock management, and comprehensive validation rules.

**Models tested:** Claude Sonnet 4.6, Claude Opus 4.6, GPT 5.4

**Conditions:**
- **WithTrellis** — AI started from the `dotnet new trellis-asp` template with copilot instructions and API reference docs. Same spec.
- **WithoutTrellis** — AI started from an empty folder with just the spec. Told to use .NET 10, ASP.NET Core, and EF Core with SQLite. Complete freedom to choose architecture and patterns.

**Evaluation:** Each implementation was reviewed by a *different* model than the one that built it (cross-review), eliminating self-review bias. Evaluation criteria were purely business-focused — no points for using specific framework patterns.

## Results at a Glance

| Metric | WithTrellis (avg) | WithoutTrellis (avg) |
|--------|:-:|:-:|
| Build succeeds | 3/3 | 3/3 |
| All tests pass | 3/3 | 3/3 |
| Test count (avg) | 62 | 65 |
| Endpoints correct | **16/16** | 13-16/16 |
| Auth vulnerabilities found | **0** | 2 of 3 |
| Cross-review score (avg) | **8.2/10** | **7.1/10** |

## Finding 1: Trellis Prevents Spec Drift

The most striking result: one WithoutTrellis implementation got **6 of 16 endpoint paths wrong** — using `/submit` instead of `/submission`, `/approve` instead of `/approval`, and so on. The spec clearly defines noun-based resource paths, but when building 16 endpoints from scratch, subtle details slip.

The same model, with Trellis, got all 16 paths correct. The template scaffolding provided the correct structure as the starting point, making compliance the default rather than a discipline requirement.

Another WithoutTrellis implementation used the wrong permission (`orders:read` instead of `orders:read-all`) on the "List Orders by Customer" endpoint — a security bug that would give sales representatives access to any customer's order history.

**WithTrellis implementations had zero spec compliance issues across all three models.**

## Finding 2: Security Fails Open Without Guardrails

Two of three WithoutTrellis implementations had authorization vulnerabilities:

- **Missing auth header defaults to admin** — When the `X-Test-Actor` header was absent or malformed, the actor provider returned an admin actor with full permissions. In production, any unauthenticated request would have complete access.
- **Error responses leak internal details** — Exception messages (including internal type names and potentially sensitive context) were written directly to HTTP responses.

WithTrellis implementations avoided both issues structurally:
- The template's `DevelopmentActorProvider` is explicitly scoped to development environments. In production, the configuration throws at startup if no real auth provider is registered — **failing closed**.
- The `ErrorHandlingMiddleware` returns a generic 500 message with only a trace ID. Internal details never reach the response.

**Authorization in Trellis is declarative and enforced by the pipeline — it's structurally impossible to forget it on a new endpoint.** WithoutTrellis implementations used manual permission checks repeated 16 times, which is consistent when disciplined but has no compiler enforcement.

## Finding 3: Cross-Review Reveals the True Gap

When models reviewed their own code, the scores were close (7.5 vs 7.5 in one case). When a *different* model reviewed the code, the gap widened:

| Code Author | Self-Review Gap | Cross-Review Gap |
|-------------|:-:|:-:|
| Opus 4.6 | Tie (7.5 vs 7.5) | **WT +1.5** (8.0 vs 6.5) |
| GPT 5.4 | WT +1.5 (8.5 vs 7.0) | **WT +0.6** (8.3 vs 7.7) |

Cross-reviewers were consistently harsher on WithoutTrellis code and identified issues the self-reviews missed (auth vulnerabilities, missing test coverage, spec compliance bugs).

## Finding 4: The Template Is the Underrated Hero

Multiple evaluators independently noted that the template scaffolding — not the framework's type system — prevented the most bugs:

> *"The three spec bugs in WithoutTrellis weren't caused by lack of effort. They were caused by writing 16 endpoints from scratch, where small details slip. The Trellis template scaffolded the correct paths and permissions from the start."*
> — Sonnet 4.6 evaluator

The template provides:
- Correct endpoint paths and HTTP methods
- Permission constants matching the spec
- API versioning by namespace convention
- Test infrastructure (fake repositories, test actor helpers, integration test fixtures)
- Copilot instructions that guide the AI through implementation order

This scaffolding eliminates an entire class of errors before any business logic is written.

## Finding 5: WithoutTrellis Wins on Day One

Every evaluator agreed: WithoutTrellis code is easier to understand for a developer seeing it for the first time. No framework vocabulary to learn, no `Result<T>` pipelines to trace, standard .NET patterns throughout.

But every evaluator also agreed: **that advantage reverses as the codebase grows.** The consistent estimate across all reviewers was that the tipping point is approximately 5 aggregates or 3+ developers.

> *"For this specific Order Management spec (3 aggregates, 16 endpoints), both approaches are equally viable. The tipping point where Trellis clearly wins is when you need to scale the codebase to 10+ aggregates, multiple teams, or complex cross-cutting concerns."*
> — Opus 4.6 evaluator

## Dimension-by-Dimension Summary

Aggregated across all 5 evaluation reports (3 self-reviews + 2 cross-reviews):

| Dimension | Winner | Unanimous? |
|-----------|--------|:-:|
| **Spec Compliance** | WithTrellis | Yes |
| **Security** | WithTrellis | Yes |
| **Code Maintainability** | WithTrellis | Yes |
| **Future Flexibility** | WithTrellis | Yes |
| **Error Handling** | WithTrellis | Yes |
| **Consistency** | WithTrellis | Yes |
| **Test Effectiveness** | WithTrellis | 4 of 5 |
| **Code Readability** | Mixed | Split |
| **Onboarding Cost** | WithoutTrellis | Yes |

## What This Means for Teams

### Choose Trellis when:
- The system will grow beyond a handful of entities
- Multiple developers will work on it over time
- Authorization rules are complex (resource-based, multi-tenant)
- You value compile-time guarantees over runtime discipline
- AI is generating code and you want reviewable, predictable output

### Skip Trellis when:
- Building a short-lived prototype or single-aggregate microservice
- The team prefers plain ASP.NET Core and doesn't want a framework learning curve
- Onboarding speed matters more than long-term maintainability

## Methodology Notes

- All implementations used .NET 10, ASP.NET Core, EF Core with SQLite
- The spec was identical for all 6 sessions — no Trellis-specific language
- WithTrellis agents received the template + copilot instructions; WithoutTrellis agents received only the spec
- Evaluation criteria were business-focused (endpoint correctness, business rules, security, test quality, maintainability) — not framework-specific
- Cross-evaluations used a different model than the one that built the code
- All source code, evaluation reports, and raw data are available for inspection
