# Changelog

All notable changes to the Trellis project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — enum rejections name the members they would have accepted

A violation carrying `enum.name-undefined` or `enum.undefined` now includes an `allowed` arg listing the permitted member names as a JSON array of strings:

```json
{ "code": "enum.name-undefined", "args": { "allowed": ["Green", "Red"] } }
```

The members were not always dropped before, but they were never available as data. `RequiredEnum.TryCreate` joined them into the English detail (`"'X' is not a valid Y. Valid values: A, B, C"`), so a client that wanted to render "choose one of…" in the caller's language had to parse an English sentence. Query-string binding was worse: its detail is the generic `"The value is not a recognized option."`, so the permitted set was unavailable by any means at all. The detail keeps its list — this is additive, and a human reading the response still gets a complete sentence.

All five producers that can reject an enum now agree: query binding (`PrimitiveConverter`), the body converter (`ScalarValueJsonConverterBase`), `RequiredEnum.TryCreate`, `RequiredEnumJsonConverter`, and a FluentValidation `IsInEnum()` rule. They route through a new `ValidationArgs.Allowed(names)` helper, which fixes both the entry name and its **ordinal** ordering in one place — the producers read their members from unrelated sources (`Enum.GetNames` versus a registry of declared statics), and nothing else would force those to line up for a client that compares or caches the list.

`enum.undefined` (a numeric value that parsed but names no member) carries the same list as `enum.name-undefined`. The remedy is identical, and covering only the name case would tell a client its options when it sent `"mauve"` but not when it sent `99`.

The arg rides on the reason code, not on the producer: a blank value reports `value.not-empty` and carries no `allowed`, since there the entry would degrade from "these are your options" into "an enum was involved somewhere".

The list is bounded. `ValidationArgs.MaxAllowedMembers` is 64; beyond it the members are dropped whole and an `allowedCount` arg is sent in their place. The 248 ISO country names cost roughly 3 KB of args on *every* rejection, and a request carrying several invalid enum fields multiplies that — a small request provoking a large response is an amplification vector, not merely waste. Truncating instead was rejected because a client cannot distinguish a shortened list from a complete one: a truncated `allowed` states that a member it omitted is not permitted, so a chooser renders the wrong set and a client validating against it rejects valid input.

The same bound now governs the prose. `RequiredEnum.TryCreate` and `RequiredEnumJsonConverter` spell every member into `Detail`, which for those 248 countries is a further 2.8 KB — more than the arg it accompanies — so above the bound the `"Valid values: …"` clause is dropped and the message reads `"'x' is not a valid Country."`, which is what the body converter already emitted. **This changes the message text for enums with more than 64 members**; narrower enums are unaffected.

The FluentValidation path reaches the same list by a different route, because FluentValidation has no placeholder for it — its message reports only the value it rejected. `ValidationArgsProjection` therefore derives the members from the attempted value's own type, and this one arg does not pass through the containment gate that governs every other. That gate asks whether a value already reached the client in the rendered message, which is the right question for an operand the application chose — a bound, a regex, a compared property — and the wrong one for member names, which are the API's own contract and the very spellings a client must send to succeed. An error code borrowed for a rule over a non-enum names nothing.

### Fixed — HTTP and ASP faults now carry a stable `Code` instead of a random GUID

Ten call sites in `Trellis.Http` and `Trellis.Asp` built their failure as `new Error.Unexpected(Guid.NewGuid().ToString("N"))`. Because the signature is `Error.Unexpected(string Code, string? FaultId = null)`, the GUID landed in **`Code`** and `FaultId` was left null — so every individual incident published a different `code` on the wire, which no dashboard can group, while the field that exists for a per-incident value went unused.

The identifier now goes to `FaultId` and the code is a stable constant. Five constants are added to `FaultCodes`:

| Constant | Wire value | Emitted when |
| --- | --- | --- |
| `HttpResponseNotSuccess` | `http.response-not-success` | Non-success status on a path that needed the body. |
| `HttpResponseNoBody` | `http.response-no-body` | `204`/`205`, or a zero-length payload. |
| `HttpResponseInvalidBody` | `http.response-invalid-body` | Body would not deserialize, or deserialized to `null`. |
| `HttpResponseFault` | `http.response-fault` | Status has no more specific mapping. |
| `ResponseLocationUnresolved` | `response.location-unresolved` | A `Location` URI could not be resolved. |

This changes the `code` value observed on the wire for these failures. Nothing could have depended on the previous values, since they were random per call. `Trellis.Http/README.md` had also documented this code as `"invalid_response_body"`, which the source never emitted.

### Changed — reason codes in documentation now follow the published vocabulary

Example code across the articles, `MIGRATION_v3.md`, and the package `SAMPLES.md` files taught reason codes the vocabulary forbids — `snake_case` spellings such as `passwords_must_match`, `invalid_format`, `payment_gateway_offline`, and `unexpected_fault` — or restated a frozen code as a bare literal (`"required"`, `"invalid"`) where a `ValidationCodes` constant exists. Examples now use the constants, and application-owned codes are namespaced.

The migration guide's concurrency example was the load-bearing case: it taught the literal `"concurrency_conflict"`, but `Trellis.Asp` maps an `Error.Conflict` to **412** only when its code is `FaultCodes.ConcurrentModification` and the request carried `If-Match`, and `ErrorRetryExtensions` classifies only that code as transient. The literal silently opted out of both.

### Changed — **BREAKING**: violation args are a closed union, so numbers reach the wire as numbers

`FieldViolation.Args` and `RuleViolation.Args` change from `ImmutableDictionary<string, string>` to `ImmutableDictionary<string, ValidationArgValue>`, a closed union of `Text`, `Number`, `Bool`, and `List`. A numeric operand is now emitted as a JSON number rather than a quoted string:

```jsonc
// before
"args": { "minLength": "3", "maxLength": "50" }
// after
"args": { "minLength": 3, "maxLength": 50 }
```

Args exist so a client can render its own localized message instead of parsing the server's English prose. Quoting every operand undercut that: a client comparing a bound against a length had to parse it back out of a string and guess at the format, which is the same recovery-by-parsing the args were introduced to end. The union keeps the property that motivated `string` in the first place — a producer still cannot hand over an arbitrary `object` and let culture-sensitive formatting leak onto the wire — because `Number` holds a `decimal` and is written invariantly.

`Number` is backed by `decimal` alone rather than one case per CLR numeric type: JSON has a single number type, so a client could not observe the distinction, and `decimal` is the widest choice that keeps integers exact. `List` is what lets a violation name a set without a producer inventing a delimiter a client would then have to know to split on.

The cases are JSON's self-describing values — its three scalars, plus a list of them. `null` and objects are deliberately absent: an arg with no value is an arg that is simply not there, so `null` would give the dictionary two spellings of one thing, and an object would require a schema per reason code to interpret, giving up the property that makes a closed union worth having and turning args into an open-ended payload to echo back to a caller. `Bool` earns its place on the same principle even though a boolean is rarely interpolated into a message: without it a producer would write `Text("true")`, reintroducing the quoted-primitive problem this change removes, and a boolean from a non-.NET producer would fail the whole payload rather than one arg.

**Migrating.** Most call sites need no change — `string`, `int`, `long`, and `decimal` all convert implicitly, so `ValidationArgs.Of("max", 255)` compiles as it did. Two things do change:

- **A numeric operand written as a quoted string keeps compiling and stays quoted.** `ValidationArgs.Of("comparisonValue", "0")` is now `Text("0")`, not a number. Drop the quotes to get the new shape. Every framework producer has been de-quoted, including the code emitted by the `Required*` source generator, so generated value objects and the built-in primitives emit numbers without any consumer action.
- **The `IFormattable` overloads are removed.** Keeping them would have made every numeric call *ambiguous*: an `int` converts to `IFormattable` by boxing and to `ValidationArgValue` by a user-defined conversion, neither target is better than the other, and `Of("max", 255)` fails to compile with `CS0121`. Their absence is what lets the implicit conversions bind. Format a value with no numeric or textual meaning of its own — a timestamp, say — explicitly and invariantly at the call site.

`ValidationArgs.Of` also gains a `params` overload taking name/value pairs, retiring the previous ceiling of two. A rule with more operands is ordinary — a scale-and-precision failure carries four — and previously had to abandon `ValidationArgs` and assemble the dictionary by hand.

**Deserializing a number rejects what it cannot hold exactly.** `Utf8JsonReader.GetDecimal()` rounds silently — `1E-100` reads back as `0`, and `0.00000000000000000000000000009` as `0.0000000000000000000000000001` — and it raises `FormatException` on overflow, which is not the exception a converter is expected to surface. Since an arg is the operand a client renders a bound from, a rounded value states a limit the producer never wrote; `ValidationArgValueJsonConverter` throws `JsonException` for such tokens instead. Exactness is judged on significant digits rather than on text, so `1e2`, `1.50` and `-0` are accepted normally. The same rule governs `Trellis.FluentValidation`'s promotion of an approved string arg to `Number`, which now requires an exact invariant round-trip before promoting.

Assertions comparing an arg to a bare string need updating: `Args["max"].Should().Be("255")` becomes `Args["max"].Should().Be(new ValidationArgValue.Number(255))`.

The FluentValidation projection's disclosure gate is **unchanged**. It still decides on the rendered string, because it decides by comparing against the message FluentValidation produced and that message is text; an arg is lifted onto the union only after it has already passed. The lift cannot admit anything the gate rejected. Enums remain `Text` — they are encoded by name, and a client matching on the name would otherwise be handed an ordinal it cannot interpret.

`ValidationArgValueJsonConverter` is public and applied to the union by attribute, so no registration is needed. It cannot be internal: the `System.Text.Json` source generator fails with `SYSLIB1220` when a trimmed or AOT-published `JsonSerializerContext` roots a violation payload whose converter it cannot access.

### Added — `WithLink(rel, href)` for RFC 8288 `Link` relations

`HttpResponseOptionsBuilder<TDomain>.WithLink(rel, href)` advertises a link relation on a response — most usefully a schema for the resource, so a client can validate before it sends rather than only learning what went wrong afterwards. Trellis previously had no way to emit a `Link` header at all: the only one was the hardcoded `next`/`prev` pair inside the pagination path.

```csharp
await result.ToHttpResponse(t => t,
    o => o.WithLink("describedby", "https://api.example.com/schemas/todo.json"));
// Link: <https://api.example.com/schemas/todo.json>; rel="describedby"
```

**`"schema"` is not a registered link relation**, so it is not what this ships. It does not appear in the IANA link-relation registry, and RFC 8288 §3.3 admits only a registered token or an absolute URI — a bare `rel="schema"` is non-conformant and generic clients ignore it. The registered spellings are `describedby` (a schema describing this resource) and `service-desc` (an API description document, RFC 8631); anything else must be an absolute URI. `WithLink` validates this at configuration time, so a malformed relation throws when the endpoint is wired rather than silently emitting a header no client honours.

The validation is also a security boundary. The relation is emitted inside a quoted string, so an unvalidated relation containing a double quote would close it early and append attacker-chosen link-params — a distinct surface from the link *target*, which is separately percent-encoded. Both now run through one `LinkHeader` helper, so consumer-configured relations and pagination cursors are escaped identically instead of the escaping living privately in the paging code.

Configured links follow the `Vary` / `Content-Language` contract rather than the `Cache-Control` one: they are **success-path only**, covering plain success, the no-payload 204, paged success (additive to `next`/`prev`, not replacing them), and `WriteOutcome`. For the same reason `WithLink` is deliberately absent from the non-generic `HttpResponseOptionsBuilder`, whose sole consumer produces a pure failure response — a test pins that absence so it is not "fixed" into an overload that could never emit.

Trellis does not generate schema documents and does not map an `OPTIONS` endpoint; `href` is whatever URL your application serves the document from.

### Changed — instrumentation helpers are named for the telemetry they register

The OpenTelemetry registration helpers were spelled four different ways: `AddResultsInstrumentation`, `AddPrimitiveValueObjectInstrumentation`, `AddTrellisMediatorInstrumentation`, and `AddTrellisValidationInstrumentation`. They now all follow `AddTrellis{Segment}Instrumentation`, where `{Segment}` is exactly the text after `Trellis.` in the source or meter the helper registers:

| Before | After | Registers |
| --- | --- | --- |
| `AddResultsInstrumentation()` | `AddTrellisResultsInstrumentation()` | `"Trellis.Results"` |
| `AddPrimitiveValueObjectInstrumentation()` | `AddTrellisPrimitivesInstrumentation()` | `"Trellis.Primitives"` |

The value is a two-way mapping: someone looking at a span tagged `Trellis.Mediator` can derive the call, and someone reading the call can derive what will appear in their backend. `AddPrimitiveValueObjectInstrumentation` broke that by naming a concept — `PrimitiveValueObject` — that appears nowhere in the emitted telemetry.

The ROP `ActivitySource` is renamed `"Trellis.Core"` → `"Trellis.Results"` to complete the mapping. The previous name was collateral from the v1 package rename (ADR-002 §2 renamed the *package* `Trellis.Results` → `Trellis.Core`, and the source silently followed) rather than a decision about the source itself. It had become actively misleading: the `Trellis.Core` package emits three telemetry names, and naming one of them after the package read as though it covered all three — while the rule would otherwise have forced `AddTrellisCoreInstrumentation()` on the deliberately high-volume, break-glass ROP source that the docs tell you to reserve for debugging. The three are now `"Trellis.Results"`, `"Trellis.Primitives"` and `"Trellis.Validation"`, each named for its role rather than for the package that ships it. As a side effect the source returns to its v1 name, so a v1 `AddSource("Trellis.Results")` subscription is correct again.

`ResultsTraceProviderBuilderExtensions.ActivitySourceName` exposes the name programmatically. Update any dashboard or `AddSource` call pinned to the literal `"Trellis.Core"`.

**Added: `ResultsTraceProviderBuilderExtensions.ActivitySourceName`.** The ROP source was the only one of the four telemetry names with no public constant — `RopTrace` is internal, and cannot be made public because it also carries test-only mutators. Six documentation sites nevertheless cited `RopTrace.ActivitySourceName`, one of them a copy-paste migration snippet that could not compile for the reader it was written for. The name is now re-exported from the public extensions class, mirroring how `TracingBehavior.ActivitySourceName` re-exports the internal `MediatorTrace`. A third `InstrumentationNamingTests` case asserts that *every* registered source or meter name is reachable as a public string member, so the next telemetry name cannot ship without one.

The rule is enforced by `InstrumentationNamingTests`, which invokes every helper against a recording builder and derives the expected method name from the source actually registered — so the telemetry name is the single source of truth rather than an approved-names table that a maintainer has to remember to update. A companion test cross-checks reflection against a repository source scan, so a helper in a package the test does not reference fails the build asking for a `ProjectReference` instead of silently escaping enforcement.

### Added — `AddTrellisValidationInstrumentation()` and the `trellis.validation.failures` counter

Trellis now publishes one `Meter`, `Trellis.Validation`, with a counter incremented once per violation as a `FieldViolation` or `RuleViolation` is created, tagged `validation.code` and `validation.violation`.

Validation failures look like user error, and that label is why nobody watches them. But server-side validation is a **backstop** — when a client enforces the same rules before sending, the counter sits near zero, and that expected value is what makes it alertable. A rising count does not mean users got worse at typing; it means client-side validation has drifted from the server's, a client broke against you, or you tightened a rule without noticing. All three are your defect arriving disguised as theirs. `validation.code` is what makes it actionable, and why a 4xx-by-route metric is not a substitute: the status says *something* drifted, the code says **which rule** did. There is deliberately no field or route tag — both are unbounded — so detection lives in the metric and diagnosis in the trace, whose JSON pointer names the field.

A second, narrower use is the framework's own: *is this rule dead?* A trace answers that only for sampled requests, so a reason code with zero volume is indistinguishable from one whose traces were all sampled away — which matters for a frozen vocabulary, where a code no producer reaches is a defect nobody can see.

**A violation is counted where the violation is created** — not at a reporting boundary, and not on the `Error.InvalidInput` that carries it. Neither alternative works. A validation failure surfaces at the HTTP boundary, at the mediator pipeline, at both, or — in a worker — at neither, so no reporting site observes each failure exactly once, and the obvious way to make them agree fails on a language detail: an `AsyncLocal` assigned inside an awaited `Send` is not visible to the caller afterwards, because a callee's assignment does not flow back up. The carrying failure is no better, for a subtler reason — it is *rebuilt* during re-projection: `JsonValidationPathRebase` re-roots pointers by constructing a fresh `InvalidInput` from an existing one's violations, and the ASP validation context aggregates collected violations into a final one, so counting there counts a single rule firing two or three times. The violation is the atom that is created once when a rule fires and only copied thereafter. A `with`-expression does not recount, because the synthesized copy constructor copies backing fields rather than re-running initializers — which is exactly what makes the rebase path free.

The consequence worth stating plainly: the counter measures rules firing, not responses sent. A violation created and then discarded is still counted. One case looks like a miscount and is not: `Trellis.Http` maps an upstream `400` or `422` into an `Error.InvalidInput` carrying `ValidationCodes.HttpBadRequest` or `ValidationCodes.HttpUnprocessableContent`, so a client process counts a failure a server produced — honestly, since the client really did construct that violation, and under two codes that never blur into locally-evaluated rules.

**Codes outside the framework vocabulary are bucketed as `other`.** An application code reaches the wire verbatim — `ValidationCodeProjection` passes through anything it does not reserve — so an application minting a code per entity or per tenant would otherwise create unbounded time series, the expensive failure mode in a hosted metrics backend. The total stays exact; only the breakdown is bucketed. The known set is read from the `ValidationCodes` constants themselves, so a code added later is tagged under its own name with no second list to drift.

As with the mediator source, the counter is inert until the helper is called and nothing reports the omission — a metric that never appears reads exactly like a rule that never fires.

### Added — `AddTrellisMediatorInstrumentation()`, so mediator spans are actually collected

`AddTrellisBehaviors()` registers `TracingBehavior`, so every command and query already calls `StartActivity` — but that returns a live activity only if a `TracerProvider` listens to the `"Trellis.Mediator"` source. Trellis.Core ships `AddTrellisResultsInstrumentation()` and Trellis.Primitives ships `AddTrellisPrimitivesInstrumentation()`; Trellis.Mediator shipped no equivalent, so the handler span was collected only by consumers who knew to type `AddSource("Trellis.Mediator")` as a raw string.

The gap is silent, which is what makes it worth closing rather than documenting. A service that never registers the source looks exactly like a service in which nothing failed: no warning, no startup error, no empty-result signal. And this is the span carrying `error.code` and `error.type` — the same values the HTTP body reports — so the missing configuration is normally discovered *during* an incident, at the moment those tags were wanted. It is also the altitude the Trellis.Core tracing guidance recommends in preference to per-`Result`-operator spans, so the package was steering people to a span it did not help them collect.

Measured before the change with an OpenTelemetry SDK exporting to Jaeger: a provider configured with both shipped helpers collected the `Trellis.Core` span and **zero** mediator spans; adding the source collected two. The Core span in the first run is the control — the pipeline was live and exporting, and the handler span alone was absent.

`TracingBehavior<TMessage, TResponse>.ActivitySourceName` keeps its value and its place in the public API. Both it and the new helper now read from one internal constant, so a helper that listens to a name nothing emits from cannot be introduced by editing one of the two — the same silent failure, one level up.

### Added — TRLDOC015, ambiguous member names must name their receiver

The `Error.Code` collapse below left ten references telling readers to use `Error`'s `ReasonCode`, a member it no longer has. All four existing doc gates passed them, and the reason is worth recording because it will recur: `FieldViolation.ReasonCode` and `RuleViolation.ReasonCode` still exist, so TRLDOC005 saw a name that resolves, TRLDOC008 saw a documented member, and TRLDOC014 saw no receiver to check — a bare name in prose carries none. Deleting a member while a same-named member survives elsewhere is precisely the move that makes every unqualified mention unverifiable.

TRLDOC015 is a fourth audit in `audit-completeness`. It reads `ambiguous-members.txt` and requires each listed name, wherever it appears in an inline code span, to be written with an owner. Scope is deliberately narrow: inline spans only (a bare name inside a fence can be a legitimate named argument), spans with a parameter list skipped (that is how a member's own declaration is documented), and ADRs excluded (a dated record is correct *because* it is stale). It is the only audit that also scans `articles/`, since that is where the motivating defect lived. Entries self-expire — once no type declares the name, the gate demands the entry's removal, because TRLDOC005 then rejects such mentions on stronger grounds.

Enabling it immediately found four more unattributed mentions in `trellis-api-asp.md` and `articles/integration-aspnet.md` that the manual sweep had missed.

### Changed — one code member, stored once, instead of four

`Error` exposed `Kind`, `Code`, `HasExplicitCode`, and `WireCode`, plus a `ValidationCodes.Normalize` rewrite rule. Three of those existed only to compensate for the fourth: `Code` was non-nullable and defaulted to `Kind`, so it could not express "the producer named no reason", so that fact had to live in a separate `HasExplicitCode`, so reading `Code` at a boundary was unsafe, so `WireCode` existed to be read instead. A member whose documentation warns you not to read it is a design defect, and it had already produced one: the mediator tagged spans with `Code` while the HTTP writer emitted `WireCode`, so the same 404 was `not-found` on the span and `error.unspecified` in the body.

`Error.Code` is now a single non-virtual `{ get; init; }` property on the base record, defaulting to `ValidationCodes.Unspecified`. `HasExplicitCode` and `WireCode` are removed; every surface reads `Code`. This does not merely discourage publishing a kind as a reason — it makes it unreachable, because the kind is no longer in the member for a boundary to leak. `HasExplicitCode`'s compile-time forcing function is replaced by a reflection test over the closed union, which fails until a newly added case is sampled and its `Code` asserted — the same enforcement without the mechanism in the public API.

Storing the code once also removes the per-case `ReasonCode` property that a first pass at this had introduced. Every case now names a reason the same way — `new Error.NotFound(resource) { Code = "account.not-found" }` — and the four cases whose reason is *required* (`Conflict`, `InvariantViolation`, `Unexpected`, `Forbidden`) take it as a positional parameter forwarded to the base, so the compiler still refuses one that says nothing. `Error.Forbidden.PolicyId` survives as a reading alias over the same storage, so the policy that refused and the code the client sees cannot drift. `Error.Equals`/`GetHashCode` are hand-written and now include `Code`; without that, two failures with different reasons would have compared equal.

`ValidationCodes.Normalize` is removed with them. It rewrote exactly one string — the pre-vocabulary `validation.error` — onto the sentinel, which contradicted this library's stated promise that an application's own codes pass through verbatim, and folded a code a producer deliberately chose onto the value meaning *nobody chose one*. `Error.TransportFault` had to override `WireCode` purely to escape it, on the reasoning that rewriting a foreign vocabulary misreports it as ours — which is true of every code the framework did not choose. `ReasonCodeVocabularyAnalyzer` already flags that placeholder at the producer, which is where it is worth catching. The `ValidationCodes.LegacyUnspecified` constant remains so the string stays documented and unreused.

`TRLS064` was extended so this rename did not silently shrink analyzer coverage: it matched on a parameter named `reasonCode`, which the mandatory-reason cases no longer have, and it never saw the `{ Code = "…" }` initializer at all. It now inspects both.

**Migration:**

| Before | After |
| --- | --- |
| `error.WireCode` | `error.Code` |
| `error.HasExplicitCode` | `error.Code != ValidationCodes.Unspecified` |
| `Error.NotFound.ForReason<T>(code, id, detail)` | `new Error.NotFound(ResourceRef.For<T>(id)) { Code = code, Detail = detail }` |
| `Error.Gone.ForReason<T>(code, id, detail)` | `new Error.Gone(ResourceRef.For<T>(id)) { Code = code, Detail = detail }` |
| `Error.RateLimited.ForReason(code, retry, detail)` | `new Error.RateLimited(retry) { Code = code, Detail = detail }` |
| `new Error.AuthenticationRequired(scheme, code)` | `new Error.AuthenticationRequired(scheme) { Code = code }` |
| `new Error.Unavailable(code, retry)` | `new Error.Unavailable(retry) { Code = code }` |
| `conflict.ReasonCode`, `invariant.ReasonCode`, `unexpected.ReasonCode` | `.Code` |
| `new Error.Forbidden(PolicyId: p)` | `new Error.Forbidden(Code: p)` — reading `.PolicyId` is unchanged |

Code that read `error.Code` expecting the kind slug — in a log line, an exception message, or a debug span tag — should read `error.Kind`, which is what it meant. Trellis's own diagnostic surfaces were updated accordingly: `UnwrapFailedException` and `Result<T>.ToString()` now name the kind, and the DEBUG-only `debug.error.code` span tag is now `debug.error.kind` on the terse `Debug` overloads, whose single error tag was always naming the case. `DebugDetailed` emits both: `debug.error.kind` for the case and `debug.error.code` for the reason the producer named. `Trellis.Core`'s `CompatibilitySuppressions.xml` returns to the repository covering the 17 intended removals above.

### Fixed — validation problems stay `application/problem+json` under `[Produces]`

A controller carrying `[Produces("application/json")]` silently downgraded Trellis validation failures from `application/problem+json` to `application/json`. `ProducesAttribute` is a result filter that rewrites `ObjectResult.ContentTypes` **wholesale**, and `ScalarValueValidationFilter` returned its problem as a plain `ObjectResult`, so the filter overwrote it. The status code and the ProblemDetails body were both unchanged, which is what made this survive: the response stopped conforming to RFC 9457 while every status-and-body assertion still passed. It was reported by a consuming team, not caught here — and the reason our suite missed it is that it asserted exactly those two invariants.

The three problem-producing sites in `ScalarValueValidationFilter` now return an internal `ProblemDetailsActionResult`, a plain `ActionResult` that executes an inner `ObjectResult`. Setting `ContentTypes` would not have helped — the result filter overwrites it — so the defence is *not being an `ObjectResult`*, which is the same reason `AsActionResult<T>` was already immune. Bodies are byte-identical, and the wrapper declares both `problem+json` and `problem+xml`, precisely the pair MVC infers for a `ProblemDetails` with an empty content-type list, so negotiation is unchanged.

Because the filter takes over **every** invalid `ModelState` — plain DataAnnotations failures, and bodies that fail to parse or convert, not only value-object rejections — an app registering it via `AddTrellisAspWithScalarValidation` now has no exposed model-validation seam. A body that never deserialized is worth calling out: nothing was semantically rejected, so it reports 400 rather than 422 and carries **no** `fieldViolations`, which makes it look untouched by Trellis when it is not.

Two things not to do about the general problem. Listing `application/problem+json` *alongside* `application/json` does not repair it — selection follows list order, so problem+json is inert anywhere but first, and any analyzer keyed on "omits problem+json" would go green on a still-broken configuration. Putting it first does repair the failure, but rewrites plain `ObjectResult` success responses to `application/problem+json`. The two safe remedies are to remove the attribute or to trim formatters via `PostConfigure<MvcOptions>`.

### Fixed — a 404 can now say what was not found, and why

`Error.NotFound`, `Error.Gone`, and `Error.RateLimited` hard-coded `HasExplicitCode => false` with no member behind it, so `code` on those responses was a compile-time constant. The sentinel exists to distinguish "the producer named no reason" from "the producer named this one", and on these three cases the second state was unreachable — every 404 the framework could produce said `error.unspecified`, whether or not the producer had something to say. A client could not tell "no such row" from "exists, but withheld from you", which are different answers to different questions.

All three now reach a reason through the inherited `Code` initializer, and so do `AuthenticationRequired` and `Unavailable`, which previously took theirs as a positional parameter. `InvalidInput` and `Aggregate` keep the sentinel by convention: their codes belong per-violation and per-child, and a root code would compete with the real ones.

Because `Code` is an inherited `init` property rather than a constructor parameter, no record's primary constructor or `Deconstruct` arity changed for the optional cases — `is NotFound(var resource)` keeps working — and silence remains the default: a producer that names nothing still reports the sentinel.

The Showcase now names its reason, so its 404 reads `"code": "account.not-found"` with a `detail` that identifies the id, rather than `error.unspecified` with no detail at all.

### Fixed — every failure response now carries the `code`/`kind` envelope it was documented to carry

The ASP reference stated the invariant plainly: "Every failure response carries top-level `code` and `kind`." It was
true of `ResponseFailureWriter` and of nothing else. Four other seams write Problem Details for requests that fail
before a handler runs, and between them they emitted the envelope inconsistently or not at all —
`ScalarValueValidationFilter` (MVC) and `ScalarValueValidationEndpointFilter` (Minimal API) emitted neither member,
`ScalarValueValidationMiddleware` emitted neither across all four of its `ValidationProblem` sites, and
`IdempotencyMiddleware` emitted `code` but not `kind`.

The effect was that the same failure class described itself two different ways depending on where it was caught. In
the Showcase, posting an invalid `Money` returned a 422 with no `code` and no `kind`, while a domain-level rejection
of the same value returned both — so a client could not branch on `kind` without first knowing which layer had
answered, which is the one thing the envelope exists to spare it.

All five emitters now derive their members from a single internal `ProblemEnvelope`, which cannot drift the way five independent call sites did. `type` is resolved there too: `IdempotencyMiddleware` hand-wrote `"type": "about:blank"` while every other seam resolved the status URI, so a `422` from before routing described itself differently from an identical `422` from a handler. Both now call `ProblemEnvelope.ProblemTypeForStatus`, which omits `type` entirely for the statuses ASP.NET Core has no default for — RFC 9457 §3.1.1 makes an absent `type` equivalent to `about:blank`, whereas a kind slug would put a bare non-URI token in a member declared to be a URI reference.

`AddTrellisProblemDetails()` closes the remaining gap by seeding the envelope on documents
ASP.NET Core itself produced — the exception handler and status-code pages — without overwriting one a Trellis writer
already supplied. Where an `Error` exists the envelope comes from the error, so a rejected value reports
`kind: unprocessable-content` even under `MapError<Error.InvalidInput>(400)` — `kind` names what failed, and moving
where a failure lands does not change what it was. Where no error was ever constructed — unparseable bytes, a
parameter the binder could not bind, a missing idempotency key — it falls back to the slug for the status.

This is additive on the wire: responses that already carried both members are byte-identical, and the rest gain
members that were documented as always present. A client that (reasonably) treated `kind` as optional is unaffected.

### Changed — the cookbook recipes now compile under Trellis's own analyzers

`Examples/CookbookSnippets` mirrors the code fences in the cookbook and is compiled by CI, which is what lets a
recipe promise it is safe to lift. It referenced the Trellis source generators but never `Trellis.Analyzers`, so the
`TRLSxxx` rules — the ones a reader's own build will apply the moment they paste a recipe in — were the one thing
never checked against the most-copied code in the documentation.

The project now references `Trellis.Analyzers` as an analyzer. Adoption cost exactly one fix across all 36 recipes:
a `Maybe<T>` probe in Recipe 8 built the expression `c => c.Email.Value` with no presence check and tripped `TRLS003`.
The analyzer was right on both paths that expression can take. `MaybeExpressionRewriter` does translate the bare form
in EF, stripping the accessor to `EF.Property`, but the storage member is nullable, so a row with no value yields
`NULL` for a non-nullable target — which is why the EF reference already called projecting `.Value` before a presence
filter unsafe. The same expression is also compiled and run in-process by `Specification` and `FakeRepository`, where
the bare form throws outright. The probe now guards the access.

No analyzer behaviour changed, and no documented shape was affected: the only `.Value` accesses in the reference are
the labelled `WRONG`/`FIX` pair in the anti-patterns file. This closes a gap the docs had already claimed was closed —
the lint reference stated that CI compiles every snippet under the repository's full analyzer settings, which was true
of the compiler's rules and not of Trellis's.

### Added — a documentation gate that makes fenced code name real members

Two documentation audits already ran in CI, and a made-up API walked through both. While `TRLS064` was being
written, two anti-pattern snippets used `Error.Validation.ForField` and `ValidationCodes.NumberOutOfRange`; neither
exists. `TRLDOC005` passed them for two compounding reasons: it validates each dotted segment **independently**, so
`Error.Validation` resolves on the strength of some unrelated `Validation` plus a real `ForField`, and it only reads
**backticked prose** — it never looked inside the fence at all. They were caught by hand, by compiling a probe.

**`TRLDOC014`** (`DocMemberAudit`, `audit-completeness`) reads the C# fences and requires that wherever the head of a
dotted chain names a real type, the next segment is a real member or nested type *of that type*. Fenced code is the
most-copied content in the doc set, which makes it the last place an invented API should be able to hide.

Resolution stops at the head of a chain, because only the head can be resolved without binding: in `order.Id.Value`,
`Id` is a property that merely shares a type's simple name. Four rules keep it silent on correct docs — inline
backticks are out of scope (prose shorthand like `DbSet.Include` is legitimate, and `trellis-api-core.md`
deliberately cites the removed v1 `Error.Validation(...)` factories in its migration table); string literals and
comments are blanked before matching; types a document declares in its own fences shadow the assemblies; and
extension methods are indexed against the type they extend. What remains genuinely needs binding and is listed in
`audit-completeness/doc-only-members.txt`, which holds four entries — inherited properties whose names match a type
(`HttpContext` inside a `ControllerBase`) and example types declared in another fence.

Like `TRLDOC013`, it refuses to pass by checking nothing: an empty type index or **zero** extracted member accesses
across the whole doc set is a failure, not a green build.

### Added — an Info diagnostic that keeps reason codes off the wire by accident

`ValidationCodes` and `FaultCodes` freeze a small set of reason codes, and Trellis dispatches on their exact wire
spelling — so the reference has always said to emit them by constant, because a typo in a literal is a silent wire
break while a typo in a constant name does not compile. Nothing enforced it.

**`TRLS064`** (`ReasonCodeVocabularyAnalyzer`, `Trellis.Analyzers`) reports a string literal in a reason-code
position that restates a frozen code, claims the reserved `error.*` namespace, or claims a namespace the framework
publishes a meaning for. `ReasonCodeVocabularyCodeFixProvider` rewrites the first shape to its constant, with
fix-all, because the motivating case is one literal repeated across dozens of call sites.

Three positions carry a reason code and all three are inspected: a `reasonCode` parameter on any Trellis method or
constructor, FluentValidation's `WithErrorCode(...)`, and `Code` on the Trellis primitive attributes. It reads the
vocabulary out of the compilation rather than carrying its own copy, so a code added to the frozen set is covered
the day it is added — and it matches by parameter or property *name* rather than by a list of members, so it covers
all eleven `ForField`/`ForRule`/`ForReason`/`For` overloads regardless of arity or argument position, plus the
twelfth the day it is added.

**It deliberately does not check vocabulary membership.** The freeze constrains Trellis, not your application, and
`trellis-api-primitives.md` promises that no analyzer pressures the choice to override a framework code or keep it.
A novel, well-formed code of your own — `order.cancel-after-ship`, or a bare `required` — is silent, including where
it is a synonym for a code Trellis also has. Only *literals* are reported: `ValidationCodes.ValueNotNull` is the
recommended shape, so matching on constant value rather than syntax would flag the fix itself.

WRONG/FIX shapes: [`trellis-api-anti-patterns.md` → TRLS064](docs/docfx_project/api_reference/trellis-api-anti-patterns.md#trls064--reason-code-literal-that-collides-with-the-frozen-vocabulary).

### Changed — **BREAKING**: a rule violation reports locations, not a bare array of pointers

`RuleViolationProblemDetail`'s third positional member changes from `string[] Fields` — a bare array of JSON Pointer
strings — to `IReadOnlyList<ViolationLocation> Locations`, and `FieldViolationProblemDetail` gains a
`ViolationLocation Location` member in place of its pointer string. `ViolationLocation` is `(string In, string?
Pointer, string? Name)`, where `In` is `body`, `query`, `path`, `header`, or `unknown`.

The old shape could only ever assert *these are pointers into the body*, which is false for a rule spanning a query
parameter or a header, and it had no member in which to say otherwise. A JSON Pointer addresses a location in a JSON
document; a query parameter is not in one, so `/pageSize` was a well-formed pointer naming something that does not
exist at that path. Because a pointer is structurally valid whether or not the body contains it, a client could not
detect the mismatch — it would resolve the pointer against the body, find nothing, and report the wrong field or none.
`Pointer` and `Name` are therefore mutually exclusive, and which one is populated follows the *addressing scheme*, not
merely "body versus everything else". `body` and `unknown` carry `Pointer`; `query`, `path`, and `header` carry `Name`,
with RFC 6901 escaping reversed so `Name` is the name the caller actually sent. `unknown` keeps the pointer because it
is the fallback for a violation raised in the domain, where the failing member is known as a path through the model
even though the request part it arrived on is not — discarding it would lose the only locating information there is.

`Locations` is always serialized, including when empty. An empty array is a positive statement that the rule is
form-level rather than bound to any field — a distinction an omitted member could not express, since absent would be
ambiguous between *no location* and *not computed*.

This is the wire-shape change that makes the request-origin feature below observable; that entry describes how the
origin is derived, while this one describes the shape it is reported in.

Clients reading `fields[i]` must read `locations[i].pointer` when `in` is `body` or `unknown`, and `locations[i].name`
when `in` is `query`, `path`, or `header`. Branch on `in` rather than testing which member is non-null: `unknown` is
the common fallback for domain-raised violations, so a client that treats "not `body`" as "has a name" silently loses
the location on exactly the failures it is most likely to receive.
### Changed — **BREAKING**: `ExpectedOutcome` gains a positional `ContentType` member

`Trellis.Testing.AspNetCore.Http.ExpectedOutcome` gains a fourth positional member, `string? ContentType`, changing its
primary constructor and `Deconstruct` signatures.

The addition exists because asserting only status and headers let a real RFC 9457 regression through: applying
`[Produces("application/json")]` rewrites a problem response's media type while leaving its status code and body
intact, so a harness that never looks at content type is structurally incapable of seeing it — which is precisely how
the `[Produces]` defect recorded above reached a consuming team rather than this suite. Making it positional keeps
`# @expect content-type:` a first-class part of the expectation rather than a bolt-on that a test can forget to opt
into.

Deliberate, and cheap here: this is a test-harness type in a `3.0.0-alpha` package.



A violation raised in the domain names the field that failed but cannot know where the value arrived from, so it
reached the wire as `location.in = "unknown"`. `Trellis.Asp` now resolves that at the response boundary from the
endpoint's own binding map: route parameters project as `path`, bound query and header parameters as `query` and
`header`, and anything left over as `body` when the endpoint binds one — otherwise `unknown` stands. Nothing is
declared and no annotation is added to the endpoint.

`[InputOrigin(...)]` and `.WithInputOrigin(...)` are added as the escape hatch for what derivation cannot reach.

Full behaviour, the two known limits, and the escape hatch: [`trellis-api-asp.md` → `InputOriginAttribute`](docs/docfx_project/api_reference/trellis-api-asp.md#inputoriginattribute).

### Added — two Info diagnostics that ask an unnamed failure to name itself

`Error.WireCode` (below) made every operator-facing channel spell a code the same way, but it cannot invent a code
that was never written. The two largest producers of application-authored failures still had no way to be told they
were producing nothing: a FluentValidation `Must(...)` and a three-argument `ValidateAdditional` both reject a value
and then report `error.unspecified`, which is indistinguishable from every other unnamed failure on the wire.

- **`TRLS063`** (`MustWithoutErrorCodeAnalyzer`, `Trellis.Analyzers`) reports a `Must(...)` or `MustAsync(...)` rule
  component that no `WithErrorCode(...)` applies to. Every built-in validator carries a name that projects to a real
  reason code; `Must` and `MustAsync` report as `PredicateValidator`/`AsyncPredicateValidator`, both of which project
  to the sentinel — and `Must` is the validator
  applications reach for most. The analyzer walks the chain only as far as the next rule component, so a code
  attached to a later `Must` does not silence an earlier one. It activates only when the compilation references
  FluentValidation, and requires that the method be declared in a `FluentValidation` namespace, that its
  receiver implement `IRuleBuilder<T, TProperty>`, and that the call resolve to FluentValidation's own built-in `Must`,
  so an application's own `Must` is never flagged. It reports only
  what it can prove: a rule whose value escapes the statement, is refined by `Configure(...)`, or passes through a
  helper the application declared could be named out of sight, and those stay silent.
- **`TRLS062`** (`RequiredPartialClassGenerator`) reports a value object that implements the three-argument
  `ValidateAdditional`, whose signature has nowhere to put a reason. The four-argument overload added alongside it
  can name the failure.

Both default to **Info**, not Warning. The shapes they flag are legal, unchanged, and widespread in existing code, so
these are prompts rather than gates — raise either with `dotnet_diagnostic.TRLS06x.severity` once a codebase has
caught up. Neither ships a code fix: only the author knows what a rule's failure should be called, and a placeholder
code reads as deliberate on the wire in a way the sentinel does not.

No wire behavior changes, and no existing code stops compiling.

### Fixed — a span and a response body now spell an error code the same way

`ResponseFailureWriter` applied `Error.HasExplicitCode` before publishing a code; `TracingBehavior` did not. Since
`Error.Code` falls back to `Kind`, an `Error.NotFound` came out as `error.unspecified` in the response body and as
`not-found` on the span. An operator handed a code in a bug report could not paste it into a trace query — which is
most of what a machine-readable code is for.

`Error.WireCode` is now the single answer to "what code does a consumer see?": the explicit code when there is one,
normalized, and the sentinel when there is not. Both the HTTP writer and the tracing behavior read it, and tests on
both sides assert against it rather than against a hard-coded string, so the two altitudes cannot drift apart again.
`Error.Code` is unchanged and remains the in-process value for a producer that needs the raw decision.

`ValidationCodes.Normalize` moved from `Trellis.Asp`'s internal `ViolationProjection` into `Trellis.Core` beside the
constants it maps, for the same reason: more than one boundary applies it, and a second copy is how two altitudes
come to disagree about the spelling of "no reason available".

**Behavior change.** The `error.code` span tag now reports `error.unspecified` for every case that carries no code of
its own — `InvalidInput`, `NotFound`, `Gone`, `RateLimited`, `Aggregate`, a bare `TransportFault`, and
`AuthenticationRequired` / `Unavailable` constructed without a `Code` — where it previously reported the kind.
Nothing is lost: the kind was always available on the `error.type` tag, and the two tags now answer two different
questions. A dashboard grouping on `error.code` to distinguish those cases should group on `error.type` instead.

The same divergence existed one layer down and is fixed the same way. `Result<T>` publishes `result.error.code` from
`WireCode` now, alongside a new `result.error.type` tag so the sentinel cases stay distinguishable, and
`LoggingBehavior`'s redacted failure summary reports the wire code and the error type — `Error.NotFound
(error.unspecified)` — rather than a raw code an operator could not find in any response body. The `debug.error.*`
tags on `ResultDebugExtensions` deliberately keep publishing the raw `Code`: that facility compiles away outside
DEBUG builds, already emits the unredacted `Detail`, and is namespaced away from the operator-facing dimensions
precisely so it can show the producer's actual decision.

`Error.TransportFault` was the last divergent case. The HTTP writer special-cased it and emitted the fault's own
code, but the error reported `HasExplicitCode` as `false` and did not override `Code`, so the span fell back to the
kind: the body said `IfMatch` and the span said `transport-fault`. `ICodedTransportFault` — a new opt-in
sub-interface of `ITransportFault` carrying `Kind` and `Code` — lets a transport package tell Core that its payload
names itself. `HttpError` implements it, and its existing members satisfy it unchanged. A transport fault's code
reaches the wire **unnormalized**, because it is the transport's word rather than a Trellis reason code. So the span
tag for an `HttpError` fault moves from `transport-fault` to the fault's own code, and for a bare `ITransportFault`
implementation from `transport-fault` to `error.unspecified`.

Two further operator-facing channels were still publishing the raw code and now use `WireCode`: the
`fieldViolations[].code` that `Trellis.Asp` synthesizes when a bound scalar fails with something other than
`Error.InvalidInput`, and the Azure Service Bus dead-letter reason (plus its matching warning log) for a message
whose envelope cannot be read.

### Added — `ValidationCodes`, a frozen reason-code vocabulary for validation failures

Trellis already carried a `code` on every field and rule violation, and every producer filled it with the same
placeholder. A client that wanted to react to *which* rule failed — highlight the offending input, retry with a
corrected value, translate the message — had only the English `detail` string to work with, so it either parsed
prose or gave up. This release fills that slot with a real vocabulary and makes roughly 210 producer sites use it.

`ValidationCodes` (namespace `Trellis`, so it arrives with `Trellis.Core`) declares the complete set as
constants: `format.*` for input that never became a CLR scalar, `string.*` for a string that arrived intact but
did not match a shape, `number.*` for an already-parsed number, `value.*` for type-agnostic presence and
comparison, `fields.*` where the subject is a *set* of fields, plus small `enum.*`, `money.*`, `http.*`,
`etag.*`, `cursor.*`, `page-size.*` and `attribute.*` families. `FaultCodes` sits beside it for the two
`Error.Unexpected` codes, which describe a failure of the system rather than of the input.

**The vocabulary's central promise is producer independence: the same failure reports the same code no matter
which layer detected it.** A value below a minimum is `value.greater-than-or-equal` whether it was caught by
query-string binding, by a generated `TryCreate`, by a hand-written primitive, or by a FluentValidation rule.
`ProducerIndependenceTests` pins that across packages, because the guarantee is worthless if it holds only by
convention — a client keying on one code and getting another from a different entry point is exactly the bug
the vocabulary exists to remove.

Codes travel with machine-readable operands where a bound is involved. `ValidationArgs.Of(...)` builds them, and
new `Error.InvalidInput.ForField`/`ForRule` overloads carry them, so a range failure reports
`args: { comparisonValue: "150" }`, a `[StringLength]` failure reports `args: { minLength: "3", maxLength: "64" }`,
and a `money.currency-mismatch` reports `args: { expected: "USD", actual: "EUR" }` rather than
burying the operand in prose. **Args are always operands, never the rejected value** — echoing the input back is
how a validation error becomes a reflected-XSS vector. (Currency codes are validated ISO 4217 symbols, not free
text, which is why they are safe to carry.)

Three failures the vocabulary deliberately keeps apart, because a client acts differently on each: an **absent**
value is `value.not-null`, a value that **arrived but is blank** is `value.not-empty`, and a value type left at
its **default** — `Guid.Empty`, `0`, `default(DateTime)` — is `value.not-default`. Every producer now checks for
blank input *before* attempting to parse, so `EmployeeId.TryCreate("")` reports `value.not-empty` rather than
`format.guid`; whitespace cannot parse into any scalar, so a `format.*` code there would name a shape the caller
never attempted. FluentValidation's `NotEmpty()` spans all three on its own, and projects to whichever one the
rejected value actually was.

FluentValidation failures project through `ValidationCodeProjection`, a 24-entry table keyed on the
`ErrorCode` **string** rather than the validator's CLR type — the two disagree in practice, since
`AspNetCoreCompatibleEmailValidator` reports `Name = "EmailValidator"`. A custom `WithErrorCode` passes through
verbatim, and `Must(...)` projects to `error.unspecified`, which is the honest answer for an arbitrary predicate.

Two boundary rules are worth knowing before you branch on a code:

- **Out-of-range-for-type is a `format.*` code, not a `number.*` one.** `int.TryParse("99999999999")` and
  `int.TryParse("abc")` both return `false`, and the producer genuinely cannot tell them apart. `number.overflow`
  is reserved for *arithmetic* overflow, such as `Money.Add`.
- **`value.not-empty` and `value.not-default` are different failures.** An empty string is empty; `Guid.Empty`
  and `0` are *default*. Likewise `enum.name-undefined` (the supplied name is not a member) is not
  `enum.undefined` (a numeric value parsed into an undefined member).

`error.unspecified` remains available and is what a producer with genuinely nothing to say emits — the
message-only `ValidationErrorsContext.AddError(string, string)` overloads, for instance. The legacy
`validation.error` placeholder is recognised and normalised at the boundary, so existing responses keep working.

**BREAKING (wire values): eight reason codes were renamed to the vocabulary's hyphen convention.** These are
values a client may already be matching on, and there is no deprecation window — the code changes with this
release:

| Old wire value | New wire value |
| --- | --- |
| `page_size.out_of_range` | `page-size.out-of-range` |
| `http.bad_request` | `http.bad-request` |
| `http.unprocessable_content` | `http.unprocessable-content` |
| `etag.parse.error` | `etag.malformed` |
| `default_initialized` | `default-initialized` |
| `unhandled_exception` | `unhandled-exception` |
| `concurrent_modification` | `concurrent-modification` |
| `not_implemented` | `not-implemented` |

The `http.*` three are the sharp edge: a caller branching on them in retry or fallback logic stops matching
**silently**, taking the default path instead of erroring. Search for the old literals before upgrading.

The control values the framework itself matches on to select HTTP behaviour — `concurrent_modification` and
`not_implemented` — are renamed too, and they are the sharpest edge of all: they are dispatch keys, so a
consumer constructing `new Error.Conflict(null, "concurrent_modification")` by hand no longer gets a `412`
on an `If-Match` request, and one constructing `new Error.Unexpected("not_implemented")` no longer gets a
`501`. Both now have constants — `FaultCodes.ConcurrentModification` and `FaultCodes.NotImplemented` —
which is the real fix: as bare literals they were invisible to the shape tests that pin every other code,
which is how they kept a spelling the convention forbids. Construct them by constant.

The transport-fault condition codes are unchanged.

**The vocabulary freezes on release.** Adding a code later is additive and safe; narrowing an existing one
degrades through namespace fallback rather than breaking a catalog entry a client already recognises.

### Added — naming a failure in the application's own vocabulary

The frozen vocabulary above is the framework's, and freezing it constrains *Trellis*, not the application. An
application whose clients already speak a catalog of its own should not have to choose between Trellis's names
and its own, so this release adds two ways to override a reason code at the declaration site. Neither is
encouraged or discouraged: no analyzer pressures either choice, and the framework code stays the default.

**Constraint attributes carry a `Code`.** `[Range]`, `[StringLength]`, `[NotDefault]`, `[Positive]`,
`[NonNegative]`, `[Negative]` and `[NonPositive]` each take an optional `Code` that replaces the framework
reason code on the resulting `FieldViolation`:

```csharp
[StringLength(8, MinimumLength = 3, Code = "account.reference.length")]
public partial class AccountReference : RequiredString<AccountReference>;
```

`[Trim]` gets none, because trimming normalizes and cannot fail. The four sign attributes share `[Range]`'s
slot, since they synthesize into the same range emission. Two consequences are stated rather than solved:
`[Range].Code` renames **both** directional failures, and the null check is never overridable — it belongs to no
attribute and always reports `value.not-null`. An empty or whitespace `Code` is the new generator error
**TRLS060**, because a blank code names nothing and would reach the wire where a client expects a catalog key.

**`ValidateAdditional` gained a four-argument overload** that can name its own failure instead of reporting
`error.unspecified`:

```csharp
static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage, ref string? errorCode);
```

The generator emits whichever declaration you implemented, so existing three-argument implementations compile
and behave exactly as before. Leaving `errorCode` unset falls back to `error.unspecified`. Declaring both
overloads is **TRLS061** — the generator emits one defining declaration, so the other implementation would fail
with a compiler error that names no Trellis concept.

### Added — `ValidationArgsOptions`, the opt-in for validator args Trellis cannot classify

`ValidationArgsProjection` publishes machine-readable operands only for validators whose placeholders Trellis
can vouch for, which means a rule Trellis did not write — a `Must()` calling
`context.MessageFormatter.AppendArgument(...)`, or any validator behind a custom `WithErrorCode` — emitted no
args at all. `ValidationArgsOptions.AllowArgs(errorCode, placeholderNames)` supplies the knowledge Trellis
lacks:

```csharp
services.Configure<ValidationArgsOptions>(options => options.AllowArgs("MinimumAge", "MinAge"));
```

`AddTrellisFluentValidation()` now calls `AddOptions<ValidationArgsOptions>()`, so the Mediator adapter always
resolves the configured instance; the standalone `ToResult` / `ValidateToResult` / `ValidateToResultAsync`
helpers have no container to read from and take the same object through an optional `argsOptions` parameter.

An explicit opt-in satisfies the **template** half of the containment gate without consulting the template — the
template check defends against a placeholder Trellis *guessed* was safe, and a custom validator has no
language-manager entry for it to consult, so leaving it in force would make the opt-in inert. **The message half
still holds**, so an opted-in arg still cannot carry anything the client's own message did not. The opt-in only
ever widens, and `PropertyValue` / `PropertyPath` can never be re-admitted: `AllowArgs` throws rather than
silently dropping them.

Generated `[StringLength]` violations now also carry `totalLength` alongside `minLength` / `maxLength`, matching
what the FluentValidation adapter already emitted for the same failure. Omitting it was a producer disagreement,
which is precisely what the vocabulary exists to remove.

### Changed — `ValidateToResultAsync` parameter order

`ValidationArgsOptions? argsOptions` was inserted **before** `CancellationToken cancellationToken`, because
CA1068 requires the cancellation token to be last. A call site that passed the token positionally as the fourth
argument no longer compiles; name it (`cancellationToken:`) to fix.


### Added — `Trellis.Messaging.AzureServiceBus`, the wire between the outbox and the inbox

Trellis shipped both ends of reliable cross-service messaging — a transactional outbox that stages integration
events with the business change, and an inbox that deduplicates them on `(ConsumerId, MessageId)` — and no
transport in between. This package is that transport.

Its central obligation is a single value. Outbox relay delivery is at-least-once, so one row can be published
more than once; the consumer's inbox only collapses those copies if they arrive under the same message id.
The publisher therefore stamps the producer's outbox row id onto `ServiceBusMessage.MessageId` verbatim. A
transport that minted its own id per attempt would leave the inbox in place, looking correct, deduplicating
nothing.

The wire format prefers standard Service Bus members over custom application properties: `MessageId` for the
dedup key and `Subject` for the event's stable wire name (from `[IntegrationEventName]`). Both are indexed by
the broker, visible in the portal and Service Bus Explorer, and usable in subscription filters, so messages
stay diagnosable and routable by tools that know nothing about Trellis. The default topology is one topic per
contract; `TopicNameResolver` collapses or prefixes that when a deployment needs something else.

Settlement follows from what the inbox reports, so `AutoCompleteMessages` is off. `Processed` and
`SkippedDuplicate` both **complete** the message — both mean it is durably accounted for, and abandoning a
duplicate would redeliver it forever because every attempt reaches the same conclusion. A handler exception
**abandons**, since the dispatcher rolled back and nothing was applied. A message that is unusable in itself —
no parseable id, no `Subject`, an unknown contract, a malformed body — is **dead-lettered immediately** with a
reason code, because retrying the same bytes cannot produce a different outcome; abandoning would only burn
the delivery count and dead-letter anyway, with no diagnosis attached.

`AddAzureServiceBusIntegrationEventPublisher` **replaces** any existing `IIntegrationEventPublisher` rather
than appending to it. In-process fan-out and broker publication are alternatives, not layers: registering both
would deliver each event locally *and* over the wire, so a service subscribed to its own topic would handle
everything twice. Replacing also makes the call order-independent.

Neither registration gets a `TrellisServiceBuilder.UseXxx()` slot, matching `Trellis.Asp.Idempotency.Cosmos`.
A builder slot is a compile-time reference, and surfacing one here would make every `Trellis.ServiceDefaults`
consumer carry the Azure SDK in order to use features unrelated to Azure.

The integration tests run against the real Azure Service Bus emulator, with a repo-owned compose file and
`Config.json` (the emulator declares entities at startup and cannot create them at runtime, so the entity list
is part of the fixture). Duplicate detection is deliberately **off** on the test topic: if the broker collapsed
duplicate ids, the suite would pass even if the transport invented a fresh id per publish. The tests skip
visibly when no emulator is reachable rather than passing against a substitute.

### Added — cross-service messaging contracts on the outbox publish seam

Groundwork that makes a message-broker adapter possible to write correctly. Both ends of reliable messaging
already ship — a transactional outbox that stages events durably, and an inbox that deduplicates them — but the
publish seam between them could not express what a transport needs.

**BREAKING: `IIntegrationEventPublisher.PublishAsync` now takes an `OutboundIntegrationMessage`.** The
bare-event overload `PublishAsync(IIntegrationEvent, CancellationToken)` is **removed**; the interface has a
single method carrying both the event and the outbox row's `MessageId`. Custom publishers change their
signature to accept the message and read `message.Event`; in-process implementations can ignore the id.

This is a correctness fix, not a convenience. Relay delivery is at-least-once, so the same row can be published
more than once, while `IntegrationEnvelope.MessageId` is specified as "the producer's outbox message id carried
verbatim by the transport". The previous signature passed only the event, so an adapter had no choice but to
mint a fresh id per attempt — putting a *different* `MessageId` on each copy, missing the consumer's
`(ConsumerId, MessageId)` dedup, and running handlers twice. The inbox would have looked correct while
guaranteeing nothing.

The bare-event overload was **removed rather than kept alongside** the new one. Keeping both (via a default
interface method) would have preserved source and binary compatibility, but it also meant an adapter that
simply did not implement the message overload would compile, run, and silently degrade the inbox — the failure
would surface as duplicate side effects in production, not as a build error. A single method makes publishing
without the identity unrepresentable.

**`IntegrationEventNameAttribute` + `IntegrationEventNameMap` give events a wire identity.** The outbox stores
`Type.AssemblyQualifiedName`, which is correct for in-process relaying and unusable across services: the consumer's
assemblies differ, and the string embeds an assembly version, so it can stop resolving after a routine version bump.
A logical name (`[IntegrationEventName("orders.order-placed.v1")]`) is owned by the contract instead, and the map
resolves it in both directions. The outbox's own storage format is unchanged — this is a wire concern only.

The map validates at construction (blank names, non-concrete types, unbound generic parameters, one name claiming
two types, one type claiming two names) because each is an unrecoverable contract bug that should surface at
startup. Lookups return `Maybe<T>` instead, because an *unknown* name is a normal operational condition: a producer
may emit contracts this consumer does not subscribe to, and the transport should dead-letter or ignore them by
policy rather than crash.

Neither duplicate-collapsing claim is overstated in the docs: carrying the id collapses redeliveries of a single
outbox row, but a retried domain row re-runs its translator and stages a genuinely new row with its own id. That
second case still needs business-identity deduplication, and
[`trellis-api-efcore-outbox.md`](docs/docfx_project/api_reference/trellis-api-efcore-outbox.md) now tabulates the
difference.

### Added — `Trellis.Asp.Idempotency.Cosmos`, a production-grade idempotency store

The first durable `IIdempotencyStore` Trellis ships. `InMemoryIdempotencyStore` is documented as unsafe across
instances and process restarts, so until now every multi-replica deployment had to write its own.

Cosmos DB maps onto the contract unusually well: `CreateItem` returns `409 Conflict` on a duplicate id within a
partition — decided on the primary replica — which is exactly the atomic reserve the contract needs; native ETags
give conditional complete and abandon without scripting; and per-item `ttl` reclaims storage with no sweeper
process. Unlike a Redis cache under `allkeys-lru`, it never silently evicts a live reservation.

Two design points are worth calling out because they are the ones that make it correct rather than merely working:

- **Session consistency is sufficient.** The read following a `409` may be served by a replica that has not yet
  seen another instance's write. That cannot cause a double execution, because the store never grants a
  reservation on the strength of a read — only an atomic create or an ETag-conditional replace grants one. A stale
  read produces `412`/`404` on the follow-up write and the operation retries. The worst observable effect is a
  spurious `AlreadyInFlight`.
- **Expiry is enforced in-process.** Cosmos DB deletes expired items on a best-effort background sweep, so an item
  can outlive its `ttl` and still be returned by a read. The store re-checks its own `reservedAt`/`completedAt`
  timestamps and treats a TTL-expired snapshot as absent; per-item `ttl` is only a storage backstop. Because
  deletion may only ever fall on a document the store's own rules have already made unreachable, a *reserved*
  document never expires — it must keep rejecting a same-key request carrying a different body — while a completed
  one expires shortly after its TTL.

The store uses the Cosmos DB *stream* APIs rather than the typed overloads, because a `409` is the normal outcome
of every replay and the typed overloads raise `CosmosException` for it — exception throwing on the hot path.

Registered with `services.AddCosmosIdempotencyStore(...)`, and provisioned with
`CosmosIdempotencyContainer.CreateIfNotExistsAsync(...)`, which sets the `/scope` partition key and enables TTL
(per-item `ttl` is ignored on a container without it). As a store registration it deliberately has **no**
`TrellisServiceBuilder.UseXxx()` slot, matching `AddInMemoryIdempotencyStore()`.

Verified by the new conformance suite against a real Cosmos DB emulator — all 17 rules, plus emulator-free tests
for the decision ordering and key encoding. Tests requiring the emulator are marked
`[Trait("Category", "Integration")]` and excluded from CI, matching the existing SQL Server-backed tests.
Idempotency keys are Base64Url-encoded into item ids because
client-supplied keys may contain `/`, `\`, `?`, or `#`, which Cosmos DB forbids in an id.

### Added — `Trellis.Testing.Idempotency`, a conformance suite for `IIdempotencyStore`

`Trellis.Asp` ships exactly one `IIdempotencyStore` — `InMemoryIdempotencyStore`, whose own documentation
states it is "not safe across multiple instances or process restarts". Every multi-replica deployment therefore
writes its own store over Redis, Cosmos DB, or a relational database. The contract those stores must satisfy is
subtle and, critically, **every violation fails silently**: a store that reserves non-atomically lets two racing
callers both execute the handler, and an `AbandonAsync` that deletes unconditionally destroys a response
`CompleteAsync` already persisted. Nothing throws; the symptom is a customer charged twice, discovered weeks later.

The new package turns the contract into an executable specification. A store author writes one class:

```csharp
public sealed class RedisIdempotencyStoreConformanceTests : IdempotencyStoreConformance
{
    protected override TimeSpan ReservationTimeout => TimeSpan.FromSeconds(2);
    protected override TimeSpan Ttl => TimeSpan.FromSeconds(4);

    protected override ValueTask<IIdempotencyStore> CreateStoreAsync(IdempotencyOptions options) =>
        new(new RedisIdempotencyStore(_multiplexer, options));
}
```

and inherits 17 rules covering reserve, replay, fingerprint mismatch, scope isolation, reservation takeover, TTL
expiry, abandon semantics, and atomicity under concurrent load. Time is handled through an `AdvanceAsync` hook, so
a `TimeProvider`-based store advances a fake clock and runs instantly while a store whose expiry a remote server
enforces shortens its timeouts and delays for real. Each test instance gets a unique `Scope`, so suites run in
parallel against shared Redis or Cosmos DB infrastructure.

`InMemoryIdempotencyStore` now runs the published suite, keeping the shipped reference implementation honest
against the same contract third parties are held to. The suite is itself covered by meta-tests that run individual
rules against deliberately broken stores and assert each rule fails, so it cannot silently degrade into a suite
that passes for everything.

One trap the package removes: `IdempotencyResponseSnapshot` is a record whose `Headers` and `Body` compare by
**reference**, so asserting snapshot equality passes only for a store that returns the very instance it was
handed. Every serialising store would fail such an assertion for no good reason. The suite exposes `ShouldMatch`,
which compares field by field.

### Added — builder slot for indirect (via) resource authorization

`TrellisServiceBuilder` gains `UseRelatedResourceAuthorization<TMessage, TLeaf, TLeafId, TOwner, TOwnerId, TResponse>(extractOwnerId)`
and `UseRelatedResourceAuthorization<TMessage, TLeaf, TOwner, TResponse>(path)`, mirroring the two existing
`services.AddRelatedResourceAuthorization(...)` overloads. Previously the builder modelled only *direct*
`IAuthorizeResource<TResource>` commands via `UseResourceAuthorization<TMessage, TResource, TResponse>()`;
commands using `IAuthorizeResourceVia<TOwner>` had no builder slot. Assembly-scanning consumers were unaffected
(`AddResourceAuthorization(assemblies)` already discovers via-commands), but AOT/trim consumers following the
AOT compatibility table found no overload that fit — and because a missing registration means *no* authorization
behavior runs for that command, the gap failed silently. The new slots register the closed-generic
`ResourceAuthorizationViaBehavior<,,,>` plus the `IAuthorizedResource<TMessage, TLeaf>` accessor, and are
order-independent relative to `UseEntityFrameworkUnitOfWork<TContext>()`.

### Fixed — repeated outbox/inbox registration no longer discards later configuration

`AddTrellisOutbox<TContext>(configure)` and `AddTrellisInbox<TContext>(configure)` registered their options with
`TryAddSingleton`, so a second call's `configure` callback was silently discarded and the first call's
configuration won. A library that registered the outbox with defaults would silently defeat an application's
later tuning (or vice versa, depending on call order). Both helpers now apply each `configure` callback on top of
the already-registered options instance and re-run `Validate()`, so a later callback wins per setting. The update is
atomic: the callback is applied to a copy and committed only after `Validate()` succeeds, so a rejected registration
cannot leave the container holding half-applied options. Configuration layers onto the *last* registered options
descriptor — the one the container actually resolves — and if a consumer owns that registration through a factory or
implementation type (which cannot be copied), the call now throws `InvalidOperationException` instead of registering a
second instance that would never reach the relay or dispatcher. The relay itself was already deduplicated —
`AddHostedService` uses `TryAddEnumerable` — so this never produced duplicate relays; the outbox reference doc
previously claimed otherwise and has been corrected.

### Changed — post-commit domain-event dispatch is no longer cancellable

`DomainEventDispatchBehavior<,>`, `TrackedAggregateDomainEventDispatchBehavior<,>`, and the
`DispatchAggregateEventsAsync` helper no longer observe the caller's `CancellationToken`, and now pass
`CancellationToken.None` to each `IDomainEventPublisher.PublishAsync` call. All three run *after*
`TransactionalCommandBehavior` has committed — `AddDomainEventDispatch` and
`AddTrackedAggregateDomainEventDispatch` re-append the transactional behavior as the innermost behavior, so
the commit happens inside `next(...)`. Previously a client disconnect mid-fan-out threw
`OperationCanceledException` between events, leaving an already-durable write with only part of its domain
events published and `AcceptChanges()` never called — a state no retry could repair, because the write had
already succeeded. Handlers that honored the token would have reintroduced the same partial fan-out one level
lower, so the token is no longer propagated to them either.

This is an observable behavior change. Dispatch now always runs to completion once the transaction commits;
a domain-event handler that must abort early has to own that decision internally. The `cancellationToken`
parameter on the public `DispatchAggregateEventsAsync` helper is retained for source compatibility but is
documented as not observed. Cascade detection and `DomainEventHandlerCascadedException` are unchanged.

### Changed — ASP route/action Location headers are now relative paths

`CreatedAtRoute(...)`, `CreatedAtAction(...)`, and `WithLocation(...)` now emit relative `Location` paths from
ASP.NET Core `LinkGenerator.GetPathByName` / `GetPathByAction` instead of absolute URIs. Relative `Location`
is explicitly permitted by RFC 9110 §10.2.2, avoids malformed `:///...` values when a request has no public
scheme/host, avoids leaking internal reverse-proxy or TLS-terminator origins, and matches the existing
`Result<WriteOutcome<T>>` response path. This is an observable HTTP behavior change for consumers that
previously received absolute URIs. Consumers that need an absolute `Location` on a 201 should use
`Created(Func<TDomain, string>)` to build the URI from the domain value, or `Created(string)` with a literal.
Note that the 200-plus-`Location` path has no literal or selector overload — `WithLocation` is route-based
only — so it always emits a relative path. Behind proxies, configure `ForwardedHeaders` and build the public
absolute URI explicitly.

### Fixed — aggregate ETag post-commit cancellation no longer reports a committed save as canceled

`AggregateETagInterceptor.SavedChangesAsync` now always syncs aggregate ETag `OriginalValue`
after a successful database commit, even if the ambient cancellation token is canceled before
the post-commit hook runs. This prevents committed async saves from surfacing as
`OperationCanceledException` and keeps subsequent saves on the same `DbContext` from using a
stale optimistic-concurrency token.

### Fixed — ROP results no longer rewrite ambient tracing span status

`Result<T>` construction and ROP operators now set OpenTelemetry status and `result.error.code` only on spans
started by Trellis's `"Trellis.Core"` ROP `ActivitySource`. In the default configuration, where
`AddResultsInstrumentation()` is not registered, recovered intermediate failures no longer mark the ambient
ASP.NET request or Mediator span as `Error`, and later `Result.Ok(...)` construction no longer resets an
existing ambient `Error` status back to `Ok`. The outer `Trellis.Mediator.TracingBehavior` span remains the
authoritative status setter for mediator dispatches.

### Fixed — mediator transaction behavior pipeline ordering is registration-order independent

`AddTrellisBehaviors()`, `AddDomainEventDispatch(...)`, and
`AddTrackedAggregateDomainEventDispatch()` now rehome pre-existing open- or closed-generic
`TransactionalCommandBehavior` descriptors so the transaction remains the innermost mediator
behavior regardless of call order. This keeps commit failures inside the standard exception,
tracing, and logging envelope and ensures domain-event dispatch stays outside the commit boundary.

### Fixed — optional HTTP JSON reader preserves Result failures for malformed JSON

`ReadJsonMaybeAsync<T>` now mirrors `ReadJsonAsync<T>` when a successful HTTP response contains malformed JSON:
it catches `JsonException` and returns `Result.Fail<Maybe<T>>(Error.Unexpected)` with sanitized line / byte
position detail instead of letting the parser exception escape. `ReadJsonOrNoneOn404Async<T>` inherits the same
behavior after its 404 check because it delegates to `ReadJsonMaybeAsync<T>`. Callers that previously caught
`JsonException` around optional JSON reads should now handle the returned failed `Result` in the normal Trellis
railway pipeline.

### Removed — `RequiredEnum<TSelf>.TryFromName` (use `TryCreate`)

`RequiredEnum<TSelf>.TryFromName(...)` has been removed. An enum's creation is a uniform symbolic
lookup, so `TryCreate` now lives on the `RequiredEnum<TSelf>` base (it is no longer generated per
type), and an enum value object's public creation surface is identical to `RequiredString` /
`RequiredInt` / etc. Direct callers of `SomeEnum.TryFromName(name)` switch to `SomeEnum.TryCreate(name)`
— same signature, same `Result<TSelf>`, same case-insensitive lookup and error message. The JSON
converter, EF Core converter, and `Parse` / `TryParse` already route through `TryCreate`.

### Added — Mediator lifetime guardrail for the authorization pipeline

`AddTrellisBehaviors()` now fails fast with a clear `InvalidOperationException` when the Mediator (`IMediator` or
`ISender`) is registered `Singleton`. Trellis's pipeline behaviors are `Scoped` — `AuthorizationBehavior` reads the per-request `Actor`
(and resource authorization the per-request loaded resource) — and a root-bound `Singleton` Mediator resolves
the pipeline from the root service provider, which cannot resolve a `Scoped` service, so the first request
would otherwise fail with an opaque dependency-injection error. The message names the fix:
`AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped)` (`Transient` also works; only
`Singleton` is rejected). The check sees the Mediator when `AddMediator` is called before `AddTrellisBehaviors`
— the canonical order in every example.

### Added — typed actor attribute accessors (`Actor.GetRequiredAttribute<TVo>` / `TryGetAttribute<TVo>`)

`Trellis.Authorization.Actor` gains two generic accessors that parse an actor attribute (an ABAC claim) into
a Trellis value object through its `IParsable` implementation, removing the `GetAttribute(...)` +
`TryCreate(...)` ceremony and the magic-string key at every call site:

- **`Result<TVo> GetRequiredAttribute<TVo>(string key)`** returns the typed value when the attribute is
  present and valid, or a failed `Result` with an `Error.InvalidInput` whose field is `key` (a missing
  attribute fails the same way) — suited to railway composition in a handler.
- **`bool TryGetAttribute<TVo>(string key, out TVo? value)`** returns `true` with the parsed value when
  present and valid, `false` otherwise — deny-closes naturally in an authorization gate.

Both constrain `TVo` to `class, IParsable<TVo>` — any source-generated `Required*<T>` value object
(`string`-, `Guid`-, `int`-backed, and so on), so a Guid-backed tenant id works as naturally as a string
claim. The existing `string? GetAttribute(string)` is unchanged. New cookbook Recipe 38 demonstrates the
tenant-isolation use: a per-command scope check with no base type.

### Added — inbox pull-consumer ergonomics (`FilterUnprocessedAsync`, dispatch outcome)

`Trellis.EntityFrameworkCore.Inbox` gains two additions that unlock the gap-free pull / anti-join consumer
model on top of the existing transactional inbox:

- **`IInboxStore.FilterUnprocessedAsync(consumerId, messageIds, ct)`** returns the subset of a candidate id
  window that the consumer has not yet processed (those without a `(ConsumerId, MessageId)` dedup row),
  preserving input order. It enables the **inbox-as-cursor / anti-join** model — scan a window of the source
  feed and dispatch every row whose `MessageId` comes back unprocessed — which is gap-free by construction,
  with no fragile high-water cursor that could skip a row committed out of sequence order. `EfInboxStore`
  implements it as a single `AsNoTracking` anti-join query; it is an optimization, not the correctness
  boundary (`DispatchAsync` still deduplicates).
- **`IInboxDispatcher.DispatchAsync` now returns `Task<InboxDispatchOutcome>`** (`Processed` vs
  `SkippedDuplicate`) instead of `Task`, so a consumer can drive metrics and checkpoint decisions without
  re-querying. Source-compatible for callers that `await` and discard the result (`Task<T>` is a `Task`); a
  custom `IInboxStore` implementation must add `FilterUnprocessedAsync`.

### Added — `WriteOutcome` static factory helpers (cast-free case construction)

A non-generic `static class WriteOutcome` (in `Trellis.Http.Abstractions`, namespace `Trellis`) now
provides `Created<T>`, `Updated<T>`, `UpdatedNoContent<T>`, `Accepted<T>`, and `AcceptedNoContent<T>`
helpers that build each `WriteOutcome<T>` case but **return the base `WriteOutcome<T>`**. Previously,
`new WriteOutcome<T>.Updated(...)` had the nested case type, and because `Result<T>` is invariant the
result could not implicitly upcast to `Result<WriteOutcome<T>>` — so pipelines had to widen it with an
explicit `(WriteOutcome<T>)` cast (e.g. `.Map(p => (WriteOutcome<Order>)new WriteOutcome<Order>.Updated(p, meta))`).
The helpers remove that cast: `.Map(p => WriteOutcome.Updated(p, meta))` now infers `T` from the value
and flows straight into `ToHttpResponse(...)`. (Forgetting the cast was usually a compile error, but the
no-body-projector path could silently bind the plain-`Result<T>` overload and skip outcome translation —
the helpers close that gap too.) Mirrors the existing non-generic `Result` / generic `Result<T>` pairing.
Purely additive; the nested-record construction still works.

### Added — `Error.InvariantViolation` resource factories (`For` / `ForReason`)

`Error.InvariantViolation` now exposes the same case-scoped convenience factories as the other
resource-bearing cases: `For<TResource>(reasonCode, id, detail)`, `For(resourceType, reasonCode, id, detail)`,
and the resourceless `ForReason(reasonCode, detail)`. Previously it was the only `Error` case that carries a
`ResourceRef` without a factory, so callers had to write
`new Error.InvariantViolation(reasonCode, ResourceRef.For<T>(id)) { Detail = ... }` by hand while the
identically-shaped `Error.Conflict` already offered `For`/`ForReason`. `reasonCode` leads the signature (it
is the invariant's required identity; the resource id is optional, matching `Error.Forbidden.For`). Purely
additive — the primary constructor is unchanged. The api-reference now documents the organizing rule:
case-scoped factories exist to remove `ResourceRef`/`InputPointer` construction ceremony, so every
resource-bearing case (`NotFound`, `Gone`, `Conflict`, `Forbidden`, `InvariantViolation`) has one, while the
resourceless cases (`AuthenticationRequired`, `RateLimited`, `Unavailable`, `Unexpected`, `TransportFault`)
intentionally use their already-minimal constructors.

### Changed — `DevelopmentActorProvider` rejects a malformed `X-Test-Actor` header by default

`DevelopmentActorOptions.ThrowOnMalformedHeader` now defaults to `true`. A malformed `X-Test-Actor`
header (invalid JSON, missing/empty/whitespace `Id`, or non-string permission entries) is a developer
error and is now **rejected** with an `InvalidOperationException` instead of silently falling back to
the configured default actor — which was a silent privilege elevation when `DefaultPermissions` is
non-empty. An **absent** or empty header is unchanged: it still yields the configured default actor
(intentional dev convenience). Set `ThrowOnMalformedHeader = false` to restore the previous lenient
fall-back-to-default behavior. Development-only — the provider already throws outside Development.

### Breaking — `Trellis.StateMachine` `FireResult` returns `Error.InvariantViolation`, and its reason code is repunctuated

`StateMachineExtensions.FireResult` (and `LazyStateMachine.FireResult`) now classify a disallowed
transition as `Error.InvariantViolation.ForReason(FaultCodes.StateMachineInvalidTransition, detail)` instead
of `Error.InvalidInput`. A rejected lifecycle transition is a domain-invariant breach evaluated against the
aggregate's current state — the trigger is well-formed; the state forbids it — so `InvariantViolation` is
the correct classification. HTTP responses are unchanged: both error types map to status 422 and share the
on-wire ProblemDetails `kind` `unprocessable-content`. What changes is the domain error type — its `Kind`
slug (`invalid-input` → `invariant-violation`) and its `Code` (now the reason code, since
`InvariantViolation.Code` returns its `ReasonCode`). Consumers that matched on the error type or read
`Error.InvalidInput.Rules` must switch to `Error.InvariantViolation` and its `ReasonCode`.

**The reason code itself also changed**, which an earlier draft of this entry said it had not:
`state.machine.invalid.transition` → `state-machine.invalid-transition`. It split two multi-word concepts
across dots, which the punctuation convention forbids — a segment hyphenates internally, it does not
subdivide. It survived because it was a bare string literal rather than a constant, so the reflection-based
guard in `ValidationCodesTests` never saw it. It is now `FaultCodes.StateMachineInvalidTransition`, and that
guard has been tightened — its shape regex allowed unlimited dot-separated segments, so it would have passed
the old spelling even as a constant. It now also caps frozen codes at `namespace.name`.

### Changed — binder/JSON value-validation status honors `MapError<Error.InvalidInput>`

Scalar- and composite-value-object validation failures raised during request binding and JSON body
deserialization now resolve their HTTP status from the configured `TrellisAspOptions`
`Error.InvalidInput` mapping (default `422`) instead of a hardcoded `422`. A single
`MapError<Error.InvalidInput>(status)` now applies uniformly across the route/query binder
(`ScalarValueValidationMiddleware`), the MVC action filter (`ScalarValueValidationFilter`), the
Minimal API endpoint filter (`ScalarValueValidationEndpointFilter`), and domain handlers — removing
the prior asymmetry where handler-level `Error.InvalidInput` was configurable but binder-level
validation was locked to `422`. Syntactically malformed JSON still returns `400` (RFC 9110 §15.5.1)
and is unaffected by the mapping. Default behavior is unchanged.

### Fixed — malformed JSON masked by a value-object validation error now returns 400

The MVC `ScalarValueValidationFilter` short-circuited to the semantic value-validation status as soon
as a scalar value object recorded a failure into the per-request `ValidationErrorsContext`, even when
the same request body also failed with a plain `JsonException` (malformed bytes). A body that first
rejected a value-object field and then hit a syntax error therefore returned `422` instead of `400`.
The filter now applies the same malformed-bytes-take-precedence guard that the structured-error path
already used, so a malformed body returns `400` (RFC 9110 §15.5.1) regardless of any value-level
failure collected earlier in the same request.

### Added — domain-event to integration-event translation (`IIntegrationEvent`)

New types for the domain-event vs. integration-event boundary, building on the transactional outbox. `IIntegrationEvent` (in `Trellis.Core`) is the published external contract, distinct from the in-process `IDomainEvent`. A domain-event handler (the *translator*) adds integration events to the scoped `IIntegrationEventCollector`; when the outbox relay re-dispatches a domain event, it drains whatever integration events the translators produced and stages them as new `OutboxMessageKind.Integration` rows, then publishes those through `IIntegrationEventPublisher` (default in-process fan-out to `IIntegrationEventHandler<T>`, swappable for a message-broker adapter). `OutboxMessage` gains a `Kind` discriminator so the relay routes domain vs. integration rows. Register via `services.AddIntegrationEventDispatch(...)` / `AddIntegrationEventHandler<TEvent, THandler>()` or the `TrellisServiceBuilder.UseIntegrationEvents(...)` slot. Integration events are emitted only after the source domain event is durably committed and dispatched; delivery is at-least-once, so consumers must be idempotent on business identity.

> **Schema change.** `OutboxMessage.Kind` maps to a new required `Kind` column (string, max length 32) on `TrellisOutboxMessages`. Adopters already running the (unreleased) outbox must add and backfill this column — generate an EF Core migration, or for an existing table backfill `Kind = 'Domain'` for current rows. New deployments (`EnsureCreated`/initial migration) get it automatically.

### Added — `Trellis.EntityFrameworkCore.Outbox` (transactional outbox)

New package providing a transactional outbox for aggregate domain events. The capture interceptor writes one `TrellisOutboxMessages` row per uncommitted domain event in the same `SaveChanges` transaction as the aggregate change, then clears the aggregate's events after the commit so the in-pipeline `DomainEventDispatchBehavior` is bypassed, and a background `OutboxRelay<TContext>` re-dispatches pending rows through `IDomainEventPublisher` after commit — durable, at-least-once, in-process dispatch. Wire it with `modelBuilder.AddTrellisOutbox()`, `optionsBuilder.AddTrellisOutboxInterceptor()`, and `services.AddTrellisOutbox<TContext>()` (or the `TrellisServiceBuilder.UseOutbox<TContext>()` slot). Each event is published in an isolated per-message scope so a handler-injected `TContext` never rides the relay's bookkeeping save. Documented limitations: a single active relay is assumed (multi-instance row-claiming is a follow-up), delivery is at-least-once so handlers must be idempotent, and persist-on-failure (`FailAfterCommit`) events are captured and dispatched by the outbox — unlike the in-pipeline dispatch, which suppresses them.

### Removed — `TRLS017` analyzer (`WrongAttributeNamespaceAnalyzer`)

`TRLS017` flagged `System.ComponentModel.DataAnnotations` `[StringLength]`/`[Range]` applied to a Trellis value object, but it could never fire on the real attributes: those attributes target properties/fields/parameters, not classes, so applying one to a value-object class is always a compile error (`CS0104` for an unqualified attribute when both namespaces are in scope, otherwise `CS0592`). The C# compiler catches it first, and Roslyn drops the invalidly-targeted attribute before the analyzer runs; the rule's only passing test relied on a stub attribute declared with `AttributeTargets.Class`, which misrepresented the real type. The analyzer, its tests, the `TRLS017` diagnostic ID, and all documentation references are removed. The rule was unshipped, so no released package exposed it — drop any `TRLS017` suppressions.

### Changed — composite value objects must have a parameterless constructor (clearer error)

A composite `ValueObject` mapped as an EF Core owned type now fails fast at model build with an
actionable `TrellisPersistenceMappingException` — naming the value object and pointing at
`[OwnedEntity]` or a hand-written private parameterless constructor — instead of EF Core's cryptic
"No suitable constructor was found for the type 'X'". This makes the long-standing requirement
explicit and uniform: a composite value object that previously relied on EF Core constructor-binding
(no parameterless constructor) now requires one. To keep a *domain* value object free of any EF Core
dependency (axiom A8), declare a private parameterless constructor yourself rather than using
`[OwnedEntity]` (which lives in `Trellis.EntityFrameworkCore`), as `Money` does.

### Changed — FluentValidation validation-error keys are now camelCase

`JsonPointerNormalizer.ToJsonPointer(...)` now lower-camelCases each name segment, so
FluentValidation-derived validation error keys match the camelCase JSON wire and the rest of
Trellis's validation field names (the seam's `Required*.TryCreate(value, fieldName)` and the ASP
scalar-binding path already camelCase). For example, `Address.PostCode` now normalizes to
`/address/postCode` (was `/Address/PostCode`) and `Items[0].Sku` to `/items/0/sku`. Indexer
segments are unchanged. Tests or clients asserting on the previous PascalCase FluentValidation
field keys need updating.

### Removed — Microservice trust-boundary code carved out to `xavierjohn/Trellis.Microservices` (BREAKING)

This release completes the carve-out of all microservice trust-boundary code (gateway-side JWT minting + consumer-side actor hydration + shared contract constants) into the separate [`xavierjohn/Trellis.Microservices`](https://github.com/xavierjohn/Trellis.Microservices) repository. The carve-out consolidates security-tier code under one CODEOWNERS surface, eliminates the gateway/consumer contract-literal duplication (now via the new `Trellis.Microservices.Abstractions` package), and lets the microservices packages evolve on an independent release cadence from the core framework.

**BREAKING** for preview-stage adopters of P3 (`TrellisInternalJwtActorProvider`) / P3.5 (Recipe 33) / P4 (`Trellis.Yarp`). Stable consumers are unaffected — no `3.0.0` GA shipped this surface.

#### Removed types

- `Trellis.Yarp` package — entire package directory. Now lives in `xavierjohn/Trellis.Microservices` under the same NuGet ID `Trellis.Yarp` (non-breaking on the package ID itself; consumer `using` directives unchanged).
- `Trellis.Asp.Authorization.TrellisInternalJwtActorProvider` — moved to `Trellis.Microservices.AspNetCore.TrellisInternalJwtActorProvider` (new package).
- `Trellis.Asp.Authorization.TrellisInternalJwtActorOptions` — moved to `Trellis.Microservices.AspNetCore.TrellisInternalJwtActorOptions`.
- `Trellis.Asp.Authorization.TrellisInternalJwtActorOptionsValidator` — moved (internal).
- `Trellis.Asp.Authorization.ServiceCollectionExtensions.AddTrellisInternalJwtActorProvider` — moved to `Trellis.Microservices.AspNetCore.ServiceCollectionExtensions.AddTrellisInternalJwtActorProvider`.
- `Trellis.ServiceDefaults.TrellisServiceBuilder.UseTrellisInternalJwtActor` slot — **DELETED outright** (no `[Obsolete]` shim). The slot in `TrellisServiceBuilder` cannot be retargeted without creating a cross-repo NuGet dependency cycle, and there is no consumer base on `3.0.0-alpha.342` that depends on it stably enough to warrant a deprecation period. Replacement is the direct `services.AddTrellisInternalJwtActorProvider(...)` extension from `Trellis.Microservices.AspNetCore`.

#### Removed documentation

- `docs/docfx_project/api_reference/trellis-api-yarp.md` — moved to `xavierjohn/Trellis.Microservices/docs/docfx_project/api_reference/trellis-api-yarp.md`.
- `docs/docfx_project/api_reference/trellis-api-cookbook.md` Recipes 33 + 34 — moved to `xavierjohn/Trellis.Microservices/docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md` (renumbered as Recipe 1 + Recipe 2). The previous slots in this cookbook now hold a forward-pointer subsection.
- `TrellisInternalJwt*` sections in `docs/docfx_project/api_reference/trellis-api-asp.md` — replaced with a forward-pointer to `trellis-api-internal-jwt.md` in the new repo, plus migration guidance.
- `UseTrellisInternalJwtActor` row in `docs/docfx_project/api_reference/trellis-api-servicedefaults.md` — replaced with guidance to use `services.AddTrellisInternalJwtActorProvider(...)` directly after installing `Trellis.Microservices.AspNetCore`.

#### Migration

| Before (this repo) | After (new repo) |
|---|---|
| `using Trellis.Asp.Authorization;` (for the `TrellisInternalJwt*` types) | `using Trellis.Microservices.AspNetCore;` |
| `<PackageReference Include="Trellis.Asp" />` (sufficient) | `<PackageReference Include="Trellis.Asp" />` **+** `<PackageReference Include="Trellis.Microservices.AspNetCore" />` |
| `services.AddTrellis(b => b.UseTrellisInternalJwtActor(...))` | `services.AddTrellisInternalJwtActorProvider(...)` (direct extension; no `TrellisServiceBuilder` slot for this provider) |
| `<PackageReference Include="Trellis.Yarp" />` | Unchanged — same NuGet ID, now published from the new repo |

The Path B "Microservices" snippet in cookbook Recipe 7 has been updated to show the new composition shape.

### Added — `AuthFailureExposurePolicy` for existence-hiding (P2)

- **New opt-in policy.** `Trellis.Mediator.ResourceAuthorizationOptions` adds a per-resource `AuthFailureExposurePolicy` ({`Propagate`, `HideAsNotFound`}, default `Propagate`). When opted in via `HideExistence<TResource>()`, the resource-authorization pipeline translates `Error.Forbidden` and `Error.AuthenticationRequired` to `Error.NotFound(ResourceRef)` so unauthorized actors cannot distinguish "resource does not exist" from "resource exists but you may not access it." Only those two error kinds translate — `Unexpected`, `Unavailable`, `NotFound` from the loader, and transport faults pass through verbatim. Translation applies to both load-failure and authorize-failure paths in `ResourceAuthorizationBehavior` and `ResourceAuthorizationViaBehavior`.
- **Projection-loader overload.** `HideExistence<TAuthorizationResource, TPublicResource>()` decouples "what the loader returns" (authorization projection) from "what the wire-public type is." The synthetic `NotFound.ResourceRef.Type` is the public type name; ID extraction tries `IIdentifyResource<TPublicResource, TId>` first, then falls back to `IIdentifyResource<TAuthorizationResource, TId>`.
- **Via commands key on `TLeaf`.** Translated NotFound references the leaf (the resource the command identifies), never the owner (an authorization implementation detail).
- **Configuration entry points.** New `services.AddResourceAuthorization(Action<ResourceAuthorizationOptions> configure)` overload and matching `TrellisServiceBuilder.UseResourceAuthorization(Action<ResourceAuthorizationOptions> configure)` builder slot. Repeated calls compose configure delegates. Every `AddResourceAuthorization` / `AddRelatedResourceAuthorization` overload now registers `IOptions<ResourceAuthorizationOptions>` unconditionally so behaviors can always resolve options regardless of registration order.
- **Observability.** Translation emits a `[LoggerMessage]` event `ExistenceHidden` (`EventId = 1`, `Level = Information`) carrying the original `Kind`, `Code`, message name, and public resource type — SecOps can audit denial reasons via SIEM without exposing the disclosure on the wire.
- **Pipeline interaction caveat.** When a command implements both `IAuthorize` and `IAuthorizeResource<T>`, the canonical pipeline's `AuthorizationBehavior` runs **before** the resource-authorization behavior. Static-permission failures from the outer behavior are NOT translated. Commands needing existence-hiding to apply to anonymous probes must omit `IAuthorize`. Documented in Recipe 32.
- **Cache safety.** Hidden 404s look identical to real 404s on the wire — a shared cache would misdirect responses across actors. Cookbook Recipe 32 calls out `Cache-Control: no-store` or `private` for protected endpoints.
- **Reference docs.** Recipe 32 (`docs/docfx_project/api_reference/trellis-api-cookbook.md`), API surface in `trellis-api-mediator.md` (`AuthFailureExposurePolicy`, `ResourceAuthorizationOptions`, and updates to both resource-authorization behaviors), new builder slot in `trellis-api-servicedefaults.md`, design rationale in `ADR-002` §5.4.
- **Constraint additions.** `ResourceAuthorizationBehavior<TMessage, TResource, TResponse>` and `ResourceAuthorizationViaBehavior<TMessage, TLeaf, TOwner, TResponse>` are now `partial` (for `[LoggerMessage]` source generator) and TMessage is annotated `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]` (preserves `IIdentifyResource<,>` metadata under trim — reflection-based ID extractor cached per closed pair). New optional constructor parameters (`IOptions<ResourceAuthorizationOptions>?`, `ILogger<...>?`) default to back-compat values (`Propagate` policy, `NullLogger`) so existing test code that manually constructs the behaviors continues to compile and run unchanged.

### Breaking — `Trellis.FluentValidation` Mediator adapter moved to new `Trellis.Mediator.FluentValidation` package

- **What moved.** `AddTrellisFluentValidation()` (and its assembly-scanning overload), `FluentValidationMessageValidatorAdapter<TMessage>`, and `FluentValidationServiceCollectionExtensions` moved from `Trellis.FluentValidation` (namespace + package) to the new `Trellis.Mediator.FluentValidation` namespace + package. Behavior is preserved bit-for-bit, including the `"Trellis.FluentValidation"` diagnostic log category (kept as-is so existing log filters work without change).
- **What stayed.** `FluentValidationResultExtensions` (`ToResult<T>`, `ValidateToResult<T>`, `ValidateToResultAsync<T>`) and `JsonPointerNormalizer` remain in `Trellis.FluentValidation`. Domain projects that only use the standalone `ValidationResult → Result<T>` helpers are unaffected and no longer pull in a transitive `Trellis.Mediator` dependency through them.
- **`JsonPointerNormalizer` promoted to public.** Previously `internal`. Promoted so `Trellis.Mediator.FluentValidation` can call across the boundary and so third-party FluentValidation adapters can reuse the RFC 6901 escape + dotted-chain segmentation rules (`Items[0].Sku` → `/Items/0/Sku`) without re-implementing them. The method itself is unchanged.
- **Migration.** For each project that calls `services.AddTrellisFluentValidation()`: add `<PackageReference Include="Trellis.Mediator.FluentValidation" />`, and change `using Trellis.FluentValidation;` to `using Trellis.Mediator.FluentValidation;` at that call site. Leave `using Trellis.FluentValidation;` in place anywhere `ValidateToResult` / `ValidateToResultAsync` / `ToResult` / `JsonPointerNormalizer` are also used. `TrellisServiceBuilder` consumers using `o.UseFluentValidation(...)` are unaffected — the builder now refs the new package internally with no call-site change required. Full recipe in `MIGRATION_v3.md` under "Trellis.FluentValidation — Mediator integration moved".

### Fixed — Round-8 audit cleanup

- **`Trellis.Asp` `IdempotencyMiddleware` releases the reservation when `CompleteAsync` fails or times out** — both catch branches around `store.CompleteAsync(...)` now call `SafeAbandonAsync(...)` after the existing log call. Previously, a transient store failure during finalization left the reservation in the in-flight state until `ReservationTimeout`, which broke the immediate-retry contract: a successful first response went non-replayable AND repeats of the same key hit `409 AlreadyInFlight` for the entire TTL window. The abandon is best-effort (its own catch logs and continues) and reuses the lazy `GetKeyHash()` local so the happy path still pays zero SHA-256 cost.
- **`Trellis.Mediator` `TracingBehavior` distinguishes consumer-initiated cancellation from errors** — when the handler throws `OperationCanceledException` and the request `CancellationToken` is canceled AND the OCE's token matches the request token, the behavior now records the exception event on the activity and sets `otel.status_description = "canceled"` WITHOUT setting `ActivityStatusCode.Error`. Genuine internal-timeout OCEs (where the OCE's token does NOT match the request token) and all other exceptions still flow through the original error path with `ActivityStatusCode.Error` + `error.type` tag + recorded exception event. OTel backends no longer mark canceled requests as errors in span dashboards.
- **`Trellis.Core.Generator` `TRLS056` release-tracking entry** — added the missing row to `Trellis.Core/generator/AnalyzerReleases.Unshipped.md`. TRLS035-039 were already present in their respective generator release files; only TRLS056 (the `Required*` member-collision diagnostic shipped in PR #564) was missing.
- **`Trellis.ServiceDefaults` README + NUGET README AOT story sync** — both READMEs previously stated the package was non-AOT, but the csproj has `IsAotCompatible=true` + `EnableAotAnalyzer=true` since an earlier round re-enabled AOT compatibility. Updated both files (plus the integration article `integration-servicedefaults.md` which had the same stale claim) to reflect the current AOT contract: explicit builder overloads are AOT-safe; scanning overloads are annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`; EF UoW remains analyzer-annotated because EF integration sits outside the AOT publish gate.
- **`Trellis.Asp.Idempotency` `IIdempotencyStore.AbandonAsync` contract tightened** — XML docs now explicitly state that implementations MUST NOT delete a snapshot that `CompleteAsync` already persisted under the same `reservationId`. This is the contract `IdempotencyMiddleware` now relies on after the AV2 fix: if a custom durable store writes the snapshot to its database and then throws during a secondary acknowledgement step, the middleware's best-effort abandon must not delete the successfully-written snapshot. The in-memory store already honored this; the contract is now explicit for third-party implementations. A regression test (`Abandon_after_Complete_persisted_snapshot_does_not_delete_the_snapshot`) was added.
- **`Trellis.Core` T4-generated extension classes — `[GeneratedCode]` removed from generated partial-class declarations** — `BindTs.g.tt`, `CombineTs.g.tt`, `WhenAllTs.g.tt`, AND `BindZipTs.g.tt` (pre-existing latent bug) previously emitted `[GeneratedCode]` at the partial-class level. C# attribute merging on partial types meant the ENTIRE merged class (including handwritten partial declarations in `Bind.cs` / `Combine.cs` / `BindZip.cs`) was being marked as generated, hiding hand-written code from analyzers and coverage tools. The attribute is now removed; proper method-level `[GeneratedCode]` emission is a follow-up scope (touches all T4 templates, requires per-method template surgery).

### Fixed — Round-7 audit cleanup

- **`Trellis.Asp` `AddTrellisIdempotency` validates options at startup** — a new internal `IdempotencyOptionsValidator` (registered via `services.AddOptions<IdempotencyOptions>().ValidateOnStart()`) now rejects invalid `HeaderName`, non-positive `MaxResponseBodyBytes` / TTL / timeouts, mismatched response status codes, empty / non-HTTP `Methods`, and invalid additional-fingerprint header names at host start with a clear `OptionsValidationException`. Previously, bad values silently flowed into request processing and surfaced as opaque runtime errors. Default configuration remains valid; no migration required for callers using `AddTrellisIdempotency()` without overrides.
- **`Trellis.Asp` `NestedJsonPathClaimsActorOptions` invariant validated at startup** — the `ContainerClaim`-required-when-`ActorIdPath`-or-`PermissionsPath`-is-set invariant was previously enforced only inside the provider constructor (failing at first request). It now also runs through an `IValidateOptions<NestedJsonPathClaimsActorOptions>` validator wired via `ValidateOnStart()`, so misconfiguration surfaces as `OptionsValidationException` at host start. The constructor guard is retained as defense-in-depth.
- **`Trellis.Testing.AspNetCore` `MsalTestTokenProvider` constructor guards** — empty `TenantId` / `ClientId` / `Scopes` previously flowed straight into MSAL and produced opaque errors. The constructor now throws `InvalidOperationException` with actionable messages naming the missing property and what MSAL needs (Azure AD tenant id, AAD application/client id, at least one scope URI). Test-only impact, but failed-test diagnostics get clearer.
- **`Trellis.Core.Generator` / `Trellis.EntityFrameworkCore.Generator` / `Trellis.Asp.Generator` generator-emitted diagnostic descriptors finished consistency sweep** — TRLS035 (Maybe partial property), TRLS036/037/038 (Owned entity), TRLS039 (scalar-value JSON converter), and TRLS056 (`Required*` member collision) now carry a `description` paragraph and a `helpLinkUri` matching the analyzer-side TRLS001-023, 054, 055 pattern from round-6. As part of the consistency sweep, the shared `HelpLinkBase` constant (one in `Trellis.Analyzers/src/DiagnosticDescriptors.cs` and four in the generator projects) was corrected from a non-resolving `/analyzers/{ID}` route to the live `/api_reference/trellis-api-analyzers.html` DocFX page (28 call sites updated to drop the trailing per-ID concatenation now that the URL is complete). Generator squiggles now have full IDE help-link parity with analyzer squiggles, and every `helpLinkUri` resolves to a real page.

### Fixed — Round-6 audit cleanup

- **`Trellis.Asp` `IdempotencyMiddleware` redacts idempotency keys in logs** — the seven `[LoggerMessage]` partial methods on the middleware previously passed the raw `Idempotency-Key` header value into structured log templates at Information/Warning/Error levels. Caller-supplied idempotency keys can encode user identity, account IDs, or other PII, so the raw values leaked into operator log sinks. Each log site now passes a redacted short SHA-256 hex prefix (12 chars / 48 bits, lowercase, with `<empty>` for missing keys); the template token has been renamed from `{Key}` to `{KeyHash}` so operators know it is a hash, not the raw value. The 12-char width keeps birthday-paradox collisions to ~0.18% per day at 1M distinct keys/day so operator correlation across log lines stays reliable. Raw keys are still used at the `IIdempotencyStore` layer so lookup/match semantics are unchanged.
- **Public XML documentation gaps** — added missing `<exception cref="ArgumentNullException">` on `Trellis.Core.Result.Fail<TValue>(Error)` and `Result.Fail(Error)` (which throw via the internal `Result<TValue>` constructor); added supporting `<param>` / `<returns>` / `<typeparam>` docs on the matching `Result.Ok` factories. Also added `<exception>` docs on `Trellis.Core.Maybe.Optional(...)` (both overloads), `Trellis.ServiceDefaults.TrellisServiceCollectionExtensions.AddTrellis(...)`, and `Trellis.Http.HttpResponseExtensions.ToResultAsync(...)` / `ReadJsonOrNoneOn404Async(...)` to cover their `ArgumentNullException.ThrowIfNull` checks.
- **`Trellis.Mediator` `TracingBehavior` records exception events on activities** — the behavior previously called `SetStatus(ActivityStatusCode.Error)` and added an `error.type` tag, then rethrew, but never recorded the exception itself on the activity. OTel backends therefore lost the exception event with `exception.type` / `exception.message` / `exception.stacktrace` tags. The behavior now calls `activity?.AddException(ex)` before the rethrow (the built-in `System.Diagnostics` API; the OTel-specific `RecordException` is not used because it is obsolete and would force a new dependency).
- **`Trellis.Core` debugger ergonomics** — added `[DebuggerDisplay]` to `ResourceRef`, `EquatableArray<T>` (with a matching `[DebuggerTypeProxy]` that exposes `Items` instead of the private `_items` backing array), `RequiredString<TSelf>`, `RequiredGuid<TSelf>`, and `RequiredEnum<TSelf>` base classes. Watch windows now show `User:abc-123`, `Length = 3`, and the underlying `Value` directly instead of expanding private fields. Added `[StackTraceHidden]` to the six `Match.InvokeAndTrace*` private helpers so that exceptions thrown by consumer `onSuccess`/`onFailure` callbacks no longer surface framework plumbing frames in their stack traces.
- **`Trellis.Core` actionable `Maybe<T>` "no value" error** — `Maybe<T>.GetValueOrThrow()` previously threw `InvalidOperationException("Maybe has no value.")`. The message now reads `"Maybe<{TName}>.Value was accessed when HasValue is false. Check HasValue first or use TryGetValue/GetValueOrDefault."` with `{TName}` substituted to the actual element type name, so consumers can navigate to the right fix without inspecting the stack trace.
- **`Trellis.EntityFrameworkCore` `TrellisScalarConverter` actionable reflection errors** — when the converter cannot find a usable `Value` property on `TModel`, the thrown messages now name the full converter generic signature, the expected `IScalarValue<TModel, TProvider>` shape, and the required `public TProvider Value { get; }` accessor. The "missing entirely" and "wrong return type" branches each give the consumer enough to fix without inspecting the converter source.
- **`Trellis.Analyzers` test brittleness** — converted 159 exact `.WithLocation(line, column)` assertions to markup-span anchors (`{|#0:matchedText|}`) across 24 analyzer and code-fix test files. Tests now resist incidental whitespace and source-layout edits while preserving the same diagnostic-location semantics. No analyzer behavior changes.

### Breaking — `Trellis.Mediator` strict snapshot domain-event dispatch with cascade detection

- **`Trellis.Mediator`** — `DomainEventDispatchBehavior<TMessage, TResponse>`, `TrackedAggregateDomainEventDispatchBehavior<TMessage, TResponse>`, and `DomainEventPublisherExtensions.DispatchAggregateEventsAsync(...)` now use a **strict single-wave snapshot** dispatch contract. The previous wave-loop with a hard cap (`MaxDispatchWaves`) silently dropped any events that exceeded the cap after logging an error; the multi-aggregate variant additionally cleared events on aggregates that did not overflow if any other aggregate in the snapshot did. Both behaviors are replaced by a single semantic: snapshot the aggregate's `UncommittedEvents()` at dispatch entry, publish only that snapshot in order, then validate that the aggregate's event queue still matches the snapshot by length and by per-position reference equality. If a handler raised new events, cleared the list via `AcceptChanges`, replaced events, or reordered them — on the same aggregate (single variant) or on any aggregate participating in tracked dispatch (multi variant) — the behavior throws the new sealed `DomainEventHandlerCascadedException` with a structured `IReadOnlyList<CascadeOffender>` (`Type AggregateType`, `IReadOnlyList<string> CascadedEventTypeNames`) listing every aggregate whose pending-event list changed. On cascade, `AcceptChanges()` is **not** called — the original snapshot stays dispatched and the cascaded events remain on the aggregate so operators can inspect. `AcceptChanges()` runs only after the validation pass proves dispatch was clean. The per-behavior `MaxDispatchWaves` constants (on `DomainEventDispatchBehavior<,>` and `TrackedAggregateDomainEventDispatchBehavior<,>`) and the internal `DomainEventDispatchDefaults` type are removed.
- **Behavior caveat — post-commit throw.** When `Trellis.EntityFrameworkCore.TransactionalCommandBehavior` is also registered, dispatch fires after the database commit, so a cascade exception always indicates that the database write is already durable while the response is failure-shaped. Consumers retrying the same command may hit "already committed" semantics. Durable at-least-once delivery requires the outbox pattern, which is planned for a future release and not shipped today.
- **Handler contract** — handlers MUST be side-effect-only and idempotent. They must not raise additional domain events on the originating aggregate, must not mutate another aggregate participating in tracked dispatch, and must not send nested Mediator commands from inside the dispatch loop (`TrackedAggregateDispatchReentrancyGuard` skips nested tracked dispatch, so nested commands can leave aggregate events stranded). The `MediatorDomainEventPublisher` continues to log-and-swallow non-cancellation handler exceptions; cascade detection covers handler-raised events, not handler-side failures.
- **Migration.** Handlers that previously relied on the wave loop to dispatch cascading events must be refactored to issue a follow-up Mediator command from the application layer after the originating command completes, or to enqueue post-commit work that runs as its own top-level command. Tests that asserted the old `Handle_RunawayHandler_CapsAtMaxWaves_AndLogsAndClears` shape now expect `DomainEventHandlerCascadedException`. Update the affected test assertions (the exception's `Offenders` list names the aggregate and cascaded event types) and remove any references to `MaxDispatchWaves`.

### Fixed — Round-4 audit cleanup

- <strong>Breaking — `Trellis.Core` `RequiredEnumJsonConverter` rejects JSON `null`</strong> — the converter now overrides `HandleNull => true` and throws `JsonException` when reading a JSON `null` token into a `RequiredEnum<TSelf>`. Previously, deserializing `"null"` into a required enum value object silently returned `null` (or `default(TSelf)`), violating the "required" contract at the JSON boundary. Migration: callers that intentionally accept null must wrap the property in `Maybe<TSelf>` or use a regular nullable enum; otherwise the new exception is the correct rejection at the request boundary. The `Write(...)` path and round-trip of valid values are unchanged.
- **`Trellis.Core` default exception mapper no longer leaks `ex.Message`** — `Result.Try` / `Result.TryAsync` previously stored raw `ex.Message` in `Error.Unexpected.Detail`. While `Trellis.Asp.ResponseFailureWriter` masks 5xx details before ProblemDetails output, non-ASP consumers (gRPC, console hosts, background jobs, test harnesses) saw the raw exception text — which routinely contains connection strings, file paths, SQL fragments, and internal schema names. The default mapper now keeps the stable `("unhandled_exception", faultId)` shape (`FaultId` for correlation) and drops `ex.Message` from the public `Detail`. The caught exception is still available to consumers that pass a custom mapper to `Result.Try(..., mapper)`. Same class of fix as PR #563 round 3 (`ExceptionBehavior`); this closes the underlying `Result.cs` site.
- **`Trellis.Core` `RequiredString<TSelf>` `StringComparison` overloads** — added `StartsWith(string, StringComparison)`, `Contains(string, StringComparison)`, `Contains(char, StringComparison)`, and `EndsWith(string, StringComparison)` overloads to let in-memory callers request ordinal (or ordinal case-insensitive) semantics. The existing single-argument overloads are unchanged — they are intentionally kept for EF Core translation. The new overloads are **not** EF Core translatable and the XML docs say so explicitly; use the single-argument overload in `IQueryable` expressions.
- **`Trellis.Analyzers` TRLS054 and TRLS055 — `Maybe<T>` IQueryable shapes the rewriter cannot translate** — `MaybeExpressionRewriter` only rewrites the binary `==`/`!=` operators. Two reasonable-looking shapes silently fall through to opaque EF translation failures: `Maybe<T>.Equals(other)` / `object.Equals(maybe1, maybe2)` inside `Where`/`OrderBy`/etc. (now TRLS054), and `HasValueWhere(capturedDelegate)` where the delegate is a variable/method group instead of an inline lambda (now TRLS055). Both diagnostics fire for `System.Linq.Queryable` method-syntax lambdas AND for C# query-expression syntax rooted at an `IQueryable<T>` source; in-memory `IEnumerable<T>` calls remain silent. TRLS054 suggests `==`/`!=`; TRLS055 suggests inlining the lambda or materializing the query first.
- **`Trellis.Core.Generator` TRLS056 — generated `Required*<TSelf>` member collisions** — the generator now scans the user's partial-class declarations before emitting members it owns (`TryCreate`, `Create`, `Parse`, `TryParse`, the primitive→value-object explicit conversion operator, `NewUniqueV4()`, `NewUniqueV7()`, `NewUniqueV7(TimeProvider)`, and the generated constructor). If the user redeclared one of those, the generator reports `TRLS056` at the user's declaration with a message naming the offending member and the base class that already provides it, and skips emitting the conflicting member. Collision detection is signature-aware: generic overloads (e.g. user `Create<T>(...)` vs generated non-generic `Create(...)`) and unrelated explicit conversions (e.g. user `operator string(MyId)` vs generated `operator MyId(Guid)`) do NOT trigger false positives. Compilation is still blocked (TRLS056 is reported as an error), but the consumer now sees an attributed Trellis diagnostic instead of an opaque `CS0111` / `CS0102` duplicate-member error from a generated file.

### Fixed — Round-3 audit cleanup

- **`Trellis.Asp` `PrimitiveJsonReader` boxing eliminated** — `TryReadKnownPrimitive<TPrimitive>` no longer routes value-type primitives through an `object? boxed` temp before casting back to `TPrimitive`. The refactor uses typed branches with a `JitCast<TActual>` helper backed by `Unsafe.As`, removing the per-deserialize box on every primitive scalar value object (`Guid`, `int`, `long`, `decimal`, `DateTime`, `DateTimeOffset`, `bool`, etc.). Reference-type primitives (`string`) are unaffected. Public behavior, error shapes, and `Maybe<TPrimitive>` semantics are unchanged.
- **`Trellis.Mediator` `LoggingBehavior` log-level discipline** — expected client / domain failures (`Error.InvalidInput`, `InvariantViolation`, `NotFound`, `Gone`, `Conflict`, `AuthenticationRequired`, `Forbidden`, `RateLimited`, and `Aggregate` instances containing only expected inner errors) now log at `Information`. `Unexpected`, `Unavailable`, `TransportFault`, and unknown / future kinds still log at `Warning`. Operators no longer get warning-level noise from normal validation, authentication, and authorization rejections. The `[LoggerMessage]` source-gen shape is preserved.
- **`Trellis.Mediator` `ExceptionBehavior` reason code is now stable** — unhandled exceptions now return `new Error.Unexpected("unhandled_exception", faultId)` with the fault GUID in the dedicated `FaultId` correlation slot instead of `new Error.Unexpected(Guid.NewGuid().ToString("N"))`, which had been putting the per-incident GUID into the stable `ReasonCode` slot. Consumers can now filter / route on `ReasonCode == "unhandled_exception"`; correlation IDs remain available via `FaultId`. The mapper shape now matches `Result.cs`'s own default exception mapping.
- **`Trellis.EntityFrameworkCore` `AddTrellisUnitOfWork<TContext>()` fail-fast on conflicting closed-generic transactional registrations** — the method's own comments promised that the dedup check recognized both open-generic and closed-generic `TransactionalCommandBehavior<,>` pre-registrations. The actual implementation only matched the open generic; users who pre-registered a closed `IPipelineBehavior<MyCmd, MyRes> → TransactionalCommandBehavior<MyCmd, MyRes>` (with or without a later open-generic registration) ended up with two transactional behaviors firing on matching commands and double commits. The helper now throws `InvalidOperationException` with an actionable message naming both supported resolutions (remove the closed registration and let the helper install the open generic, or call `AddTrellisUnitOfWorkWithoutBehavior<TContext>()` to keep explicit closed registrations and skip open-generic installation). The open-generic-only idempotent path is unchanged.
- **`Trellis.ServiceDefaults` / `Trellis.Asp` nested-JSON-path actor-provider configure is now optional** — `UseNestedJsonPathClaimsActorProvider(...)` and `AddNestedJsonPathClaimsActorProvider(...)` now accept `Action<NestedJsonPathClaimsActorOptions>? configure = null`, matching the sibling claims / Entra / development actor-provider helpers. Default options (flat claims) are applied when `configure` is omitted.
- **`Trellis.Primitives` `RequiredGuid<TSelf>.NewUniqueV7(TimeProvider)`** — new overload accepts an explicit `TimeProvider` so tests can use `FakeTimeProvider` for deterministic v7 GUID timestamps. The previously flaky V7-ordering test in `RequiredGuidTests` (which relied on `Thread.Sleep(2)` and Windows millisecond-clock granularity) now uses the new overload and is deterministic. The generator emits the new overload for every `RequiredGuid<TSelf>` derivation; no migration is required.
- **`Trellis.Core` `Result<T>` raw-JSON example drift** — the `README.md`, `NUGET_README.md`, and `ResultRequiresExplicitHttpMappingConverter` XML doc all showed a stale `{"IsSuccess": true, "Value": ..., "Error": null}` example for raw `Result<T>` serialization. `Result<T>` has no public `Value` property; the example now reflects what raw serialization could actually produce (`{"IsSuccess": true, "IsFailure": false, "Error": null}` — public state only, no success value) and reinforces that consumers should use `Match` / `TryGetValue` / `Deconstruct` or the HTTP-mapping path via `Trellis.Asp`.

### Fixed — Round-2 audit cleanup

- **Input-size caps** — `Trellis.Asp` idempotency keys, `Trellis.Primitives` phone numbers, and `Trellis.Core` cursor tokens now reject oversized inputs before parsing, normalization, or decoding: idempotency header values above 4 KiB, phone-number input above 32 characters, and cursor tokens above 1024 characters. Rejections preserve the existing invalid-key / invalid-input / `cursor.malformed` failure shapes while hardening DoS paths. No migration is required.
- **Diagnostic logging for response-resource synthesis** — `Trellis.Asp` `ResponseFailureWriter` now emits a throttled warning once per exception type when service resolution or `ResourceCollectionNameRegistry.Resolve` throws while synthesizing a 404 / 409 `ProblemDetails.Instance`. The safe fallback to the request URL is unchanged, so response mapping still never turns those failures into 500s; previously silent misconfiguration is now actionable. The logger is optional and no-ops when none is registered.
- **Async-only actor-provider disposal** — `Trellis.Asp` `WorkerComposedActorProvider.Dispose()` no longer blocks on `DisposeAsync()` for inner providers that only implement `IAsyncDisposable`. Sync disposal now skips async-only inners and emits a throttled warning telling consumers to use `DisposeAsync()` / `await using`; the async disposal path remains the correct cleanup path.
- **EF interceptor cancellation** — `Trellis.EntityFrameworkCore` aggregate ETag and timestamp async interceptors now honor pre-canceled tokens before local mutation work, so canceled saves do not assign ETags or timestamps.
- **Behavior change — `Trellis.Testing` `FakeRepository` parity** — duplicate-key failures now use the canonical `"duplicate.key"` reason and not-found details now quote the ID and include a period, matching the EF repository runtime. Tests that asserted the old fake-only strings (`"duplicate.unique.constraint"` or `with ID {id} not found`) should update to the canonical shape they would assert against EF.
- **Log-injection hardening for enum JSON errors** — `Trellis.Core` `RequiredEnumJsonConverter` now sanitizes invalid enum names in `JsonException` messages by truncating long values and escaping control characters, preventing log injection and exception-message bloat when middleware logs parse failures verbatim.

### Fixed — Post-audit bundle: EF interceptor idempotency, scalar JSON generator nesting, `FireResult` guard, FluentValidation diagnostics

- **`Trellis.EntityFrameworkCore` `AddTrellisInterceptors(...)` idempotency** — repeated calls on the same `DbContextOptionsBuilder` now install Trellis' query / save interceptors exactly once, using an internal `IDbContextOptionsExtension` marker to detect prior registration. Library-level and application-level composition can both call the helper without double-firing Maybe-equality rewrites, scalar-value rewrites, or timestamp rewrites per query/save; consumer interceptors registered separately via `optionsBuilder.AddInterceptors(...)` are preserved. No migration is required.
- **`Trellis.Asp` scalar-value JSON converter generator** — nested `JsonSerializerContext` declarations now regenerate the containing-type chain instead of emitting a conflicting top-level partial class, so contexts such as `Outer.InnerContext` compile correctly. Source-generator hint names now include the full namespace and containing-type chain, preventing same-named contexts in different namespaces or owners from colliding. No migration is required.
- **Behavior change — `Trellis.StateMachine` `FireResult(...)`** — `FireResult` now checks `CanFire(trigger)` before calling Stateless `Fire`, returning a typed `Error.InvariantViolation` for impermissible transitions without invoking the consumer's `OnUnhandledTrigger` callback. This keeps guarded result-based transitions from running side-effect callbacks and avoids confusing custom `InvalidOperationException` throws with Stateless' default unhandled-trigger exception; guard exceptions still surface. Consumers that intentionally rely on `OnUnhandledTrigger` side effects for rejected transitions should call `Fire` directly instead of `FireResult`.
- **`Trellis.FluentValidation` scanner diagnostics** — assembly scanning now reports `ReflectionTypeLoadException` details when validator discovery has to drop types because transitive dependencies are missing. If an `ILoggerFactory` is registered, the scanner emits one warning per affected assembly with the assembly name, dropped-type count, and a sample of loader-exception messages; otherwise it falls back to `Debug.WriteLine`. The diagnostics are non-breaking and make previously silent partial discovery failures actionable.

### Fixed — Post-audit cleanup: resource-auth idempotency, TRLS009 fix correctness, PhoneNumber.GetCountryCode totality, doc sweep

- **`Trellis.Mediator` resource authorization** — closed `ResourceAuthorizationBehavior<TMessage, TResource, TResponse>` registrations are now idempotent when the existing descriptor has the same `ServiceType` and `ImplementationType`. Repeated typed registration, repeated assembly scanning, and explicit-plus-scanned overlap no longer double-load resources, double-run authorization, or fire the same pipeline behavior twice per request. Distinct closed behaviors, including different response types, continue to coexist. No migration is required; this only removes duplicated registrations that were always a bug.
- **`Trellis.Analyzers` TRLS009 code fix** — the "use async method variant" fix now rewrites the invocation to the async overload, wraps it in `await`, and adds `async` to the enclosing `Task` / `ValueTask` method or local function when that conversion is locally safe. The analyzer still reports unsafe shapes, but the fixer no longer offers a broken fire-and-forget rewrite for synchronous `void` / value-returning methods or non-async lambdas; those cases require manual conversion to an async call chain.
- <strong>Breaking — `Trellis.Primitives` `PhoneNumber.GetCountryCode()`</strong> — now returns `Maybe<string>` instead of `string`. Numbers that satisfy E.164 shape validation but do not start with an assigned ITU-T country calling code now return `Maybe<string>.None` instead of throwing `InvalidOperationException`, restoring the value-object contract that a constructed value is safe for all queries. Migration: replace `var code = phone.GetCountryCode();` with `if (phone.GetCountryCode().TryGetValue(out var code)) { ... }` or use `phone.GetCountryCode().GetValueOrDefault(...)` when a fallback is appropriate.
- **XML docs, examples, and compile-checked snippets** — refreshed stale examples across Core, Authorization, Cookbook snippets, testing-pattern guidance, and the Core API reference so documented shapes compile against the current framework. The sweep updates obsolete `Result.Value` / implicit-failure examples to `TryGetValue` or `Match`, replaces removed `ICommand<Result>` examples with `ICommand<Result<Unit>>`, modernizes scalar-value examples to `RequiredString<TSelf>` / `RequiredGuid<TSelf>` and `IScalarValue<TSelf, TPrimitive>`, and points aggregate event persistence guidance at the tracked-aggregate domain-event dispatch behavior.

### Breaking — Required<T> defaults flipped to LENIENT-by-default; `[NotDefault]`/`[Trim]` are the new opt-ins

This is the canonical v3 `Required*` default behavior.

- **`Trellis.Core` / `Trellis.Primitives`** — every `Required*<T>` base is now **lenient by default**: rejects only `null`. Every concrete value is accepted: `0` (int/long/decimal), `Guid.Empty`, `DateTime.MinValue` / `DateTimeOffset.MinValue`, `""`, and whitespace-only strings. Strings are **not** auto-trimmed.
- **`[NotDefault]` is now the meaningful opt-in** to reject the type's sentinel (`0` / `Guid.Empty` / `MinValue`). For `RequiredString<T>`, `[NotDefault]` rejects `null` + `""` (and whitespace-only when combined with `[Trim]`).
- **`[Trim]` is now the meaningful opt-in** for string trimming. With `[NotDefault]`, whitespace-only input trims to `""` and is rejected. Without `[NotDefault]`, trimming normalizes the stored value only.
- **Deleted attributes** — `[AllowZero]`, `[AllowEmpty]`, `[AllowMinValue]`, `[AllowWhitespace]`, and `[NoTrim]` are **removed entirely**. Remove any usage; they no longer compile.
- **Retired diagnostics** — `TRLS048`–`TRLS053` (which policed the deleted attributes) are retired. `TRLS046` ("`[NotDefault]` is vestigial") and `TRLS047` ("`[Trim]` is vestigial") are removed — those attributes are now meaningful opt-ins.
- **`ActorId`** (`Trellis.Authorization`) carries `[Trim, NotDefault]` to preserve its previous strict + trim behavior.
- **Migration.** Add `[NotDefault]` where sentinel rejection is required; add `[Trim]` where trimming is required. For types that previously used `[AllowEmpty]` / `[AllowZero]` / `[AllowMinValue]` / `[AllowWhitespace]` / `[NoTrim]`: those leniencies are now the default — simply remove the deleted attributes. Full guidance: [MIGRATION_v3.md](MIGRATION_v3.md#requiredt-defaults-lenient-by-default-with-notdefault--trim-opt-ins).

### Changed — `OverloadResolutionPriority` retires CS0121 ambiguity on async Result extensions

- **`Trellis.Core`** — applied `[OverloadResolutionPriority(1)]` to the Task-delegate overloads of `BindAsync` / `MapAsync` / `TapAsync` / `TapOnFailureAsync` / `MapOnFailureAsync` / `CheckAsync` / `CheckIfAsync` / `EnsureAsync` / `MatchAsync` / `BindZipAsync` on sync `Result<T>` receivers (both non-tuple and T4-generated tuple variants). Inline `async` lambdas whose body returns a synchronous `Result<R>` no longer surface the historical `CS0121` ambiguity between the Task and ValueTask delegate overloads — the Task overload now wins by priority. Strongly-typed `Func<T, ValueTask<Result<R>>>` delegates continue to resolve to the ValueTask overload because the Task overload is not applicable (priority only ranks applicable candidates). Doc trap callout and the CS0121 row in `trellis-api-core.md` are retired; replacement note explains the new resolution rule and the one remaining LINQ `SelectMany` cross-class limitation that Roslyn does not currently disambiguate via priority.
- **`Trellis.Core`** — `[OverloadResolutionPriority(1)]` is also applied to the sync-receiver Task-delegate `SelectMany` overload (`Trellis.Core/src/Result/Extensions/Linq.Task.Right.cs`) for forward-compatibility, even though Roslyn does not currently honor priority across distinct extension classes (the Task `SelectMany` is in `ResultLinqExtensionsTaskRightAsync`, the ValueTask `SelectMany` is in `ResultLinqExtensionsValueTaskRightAsync`). LINQ query syntax over async Result composition still benefits from a typed local delegate when the async return type is ambiguous.
- **Test infrastructure** — new `OverloadResolutionPriorityTests` class exercises each affected method with the previously-ambiguous inline-async-lambda shape and asserts both the return type (Task vs ValueTask) and runtime semantics. A reflection-based safety net (`OverloadResolutionPriority_AppliedConsistently_AcrossSyncReceiverTaskDelegateOverloads`) walks every sync-receiver Task-delegate `XxxAsync` method that has a sibling ValueTask overload and fails if any is missing the attribute — so a future contributor who adds a new method (or removes the attribute from an existing one) gets a focused test failure with the offending method name instead of a downstream CS0121 from a random caller.

### Breaking — `AddTrellisAsp()` no longer auto-registers scalar-value validation

- **`Trellis.Asp`** — `AddTrellisAsp()` previously called `AddScalarValueValidation()` unconditionally, silently mutating global `MvcOptions` and `JsonOptions` (model binders, JSON converters, `SuppressModelStateInvalidFilter` flip) without user-facing disclosure. The mutation was invisible from the call site and surprised consumers who had already configured their own converters / naming policies. In v3, `AddTrellisAsp()` registers ONLY `TrellisAspOptions` (error-to-status-code mapping), `ResourceCollectionNameRegistry`, and the composition contract for layered `MapError<TError>` configuration. Scalar-value validation is now an explicit opt-in.
- **`Trellis.Asp`** — new convenience helpers `services.AddTrellisAspWithScalarValidation()` and `services.AddTrellisAspWithScalarValidation(Action<TrellisAspOptions>)` compose `AddTrellisAsp` and `AddScalarValueValidation` in one call. Greenfield controller hosts that bind value-object DTOs from JSON / route / query should use the convenience helper; the behavior matches the v2.x default exactly. Hosts that only need error mapping (no VO DTO binding) call `AddTrellisAsp()` alone and skip the binder / converter mutation.
- **`Trellis.ServiceDefaults`** — `TrellisServiceBuilder.UseAsp(...)` no longer implies scalar-value validation. New slot `TrellisServiceBuilder.UseScalarValueValidation()` (applied after `UseAsp`, before `UseProblemDetails`) opts a host into the scalar-value model binders, JSON converters, and `SuppressModelStateInvalidFilter` toggle for both MVC and Minimal API JSON pipelines. The slot is idempotent and independent of `UseAsp()` so MVC sites that don't bind VO DTOs can stay on `UseAsp()` alone. Minimal API hosts must still call `app.UseScalarValueValidation()` middleware and chain `.WithScalarValueValidation()` per endpoint.
- **Migration.** The two-shape mechanical migration is in [MIGRATION_v3.md](MIGRATION_v3.md#trellisasp-v3--addtrellisasp-no-longer-auto-registers-scalar-value-validation). For behavior-preserving migration: replace every `services.AddTrellisAsp(` with `services.AddTrellisAspWithScalarValidation(` and append `.UseScalarValueValidation()` after every `.UseAsp()` in `AddTrellis(o => ...)` composition roots.

### Documentation — owner-by-id authorization quick-start

- **`docs/docfx_project/api_reference/trellis-api-authorization.md`** — new "Owner check quick-start — copy this" section at the top of the document, before the Patterns Index. Names the canonical owner-on-loaded-resource pattern (`IAuthorizeResource<TResource>` + `IIdentifyResource<TResource, TId>` so the framework reuses `SharedResourceLoaderById<TResource, TId>`) and shows the complete copy-paste setup. The Patterns Index gains a top-of-table row pointing back at the quick-start.
- **`docs/docfx_project/api_reference/trellis-api-cookbook.md`** — Recipe 7 reordered so the canonical owner case appears first (with explanatory comments naming it as "the 90% case"), then the static-permission gate. Recipe 24's decision matrix gains an explicit "Owner-by-id check on the command's resource (the 90% case)" row at the top so consumers debugging an authorization path land on the simplest match first.

### Documentation — `FailAfterCommit` composition anti-pattern

- **`Trellis.Core`** — `Result.FailAfterCommit<T>(error)` XML remarks now explicitly call out that the helper is a *leaf* worker-handler operation and must not be threaded through `Combine` / `TraverseAll` / `SequenceAll` / `WhenAllAsync`. The `PersistOnFailure` flag OR-accumulates onto aggregated failures, which silently commits the staged permanent-failure mutation alongside any other legs' outcomes — almost never what the handler author intended. The new guidance directs authors to restructure such handlers so the aggregating step runs to its terminal outcome first and `FailAfterCommit` is invoked as a terminal step (or in a follow-up command).
- **`docs/docfx_project/api_reference/trellis-api-anti-patterns.md`** — new "Result.FailAfterCommit composed with aggregating operators" entry with a WRONG / FIX gallery example.
- **`docs/docfx_project/api_reference/trellis-api-core.md`** — IPersistOnFailure section gains an "Anti-pattern" subsection pointing at the new anti-pattern entry, plus the existing pipeline-composition table.

### Added — Silent-403 diagnostics and `NestedJsonPathClaimsActorProvider`

- **`Trellis.Asp`** — `ClaimsActorProvider` now emits two startup-diagnostics log entries (each throttled to fire at most once per application lifetime) that surface the silent-403 footgun caused by mis-configured nested-JSON identity-provider claims (Auth0 `app_metadata.roles`, Azure B2C `extension_*`, some Okta token shapes): **EventId 2** (Warning) fires when the configured `PermissionsClaim` resolves to zero entries on an authenticated identity that carries other claims; **EventId 3** (Error) fires when the configured claim resolves to a single value that parses as a JSON object or array. The diagnostics name the configured claim, list a sample of present claim types, and recommend `NestedJsonPathClaimsActorProvider`.
- **`Trellis.Asp`** — new `ClaimsActorOptions.ValidateClaimShapeOnFirstUse` toggle (default `true`) suppresses both diagnostics when set to `false`. Use only when the diagnostics duplicate an existing health-check or claim-validation pipeline.
- **`Trellis.Asp`** — new `NestedJsonPathClaimsActorProvider` and `NestedJsonPathClaimsActorOptions` for identity providers that ship roles/permissions under a nested JSON claim. Configure with a `ContainerClaim` naming the top-level claim that carries the JSON payload plus optional `ActorIdPath` / `PermissionsPath` dotted JSON paths. Terminal elements may be strings (single value), arrays of strings (multiple values), or objects whose property names are emitted as the values (Auth0 roles-as-object shape). Falls back to the inherited flat-claim resolution when the configured paths are empty, when the container claim is absent, or when its value fails to parse as JSON (with a one-off **EventId 4** warning). Register via the new `services.AddNestedJsonPathClaimsActorProvider(opts => ...)` extension. AOT-safe (uses `JsonDocument.Parse`).

### Added — `Trellis.ServiceDefaults` AOT compatibility

- **`Trellis.ServiceDefaults`** — re-enabled `IsAotCompatible=true`, `IsTrimmable=true`, `EnableAotAnalyzer=true`, `EnableTrimAnalyzer=true`. The package is now AOT- and trim-compatible.
- **`Trellis.ServiceDefaults`** — new AOT-safe per-type overloads on `TrellisServiceBuilder`: `UseFluentValidation()` (adapter only) + `UseFluentValidation<TValidator, TMessage>()` per validator; `UseResourceAuthorization()` (pipeline only) + `UseResourceAuthorization<TMessage, TResource, TResponse>()` per command; `UseDomainEvents()` (publisher + behavior only) + `UseDomainEvents<TEvent, THandler>()` per handler; `UseTrackedAggregateDomainEvents()` (publisher + behavior only) + `UseTrackedAggregateDomainEvents<TEvent, THandler>()` per handler. The four existing assembly-scanning overloads are now annotated with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` so the AOT analyzer surfaces the choice between AOT-safe and scanning shapes at the consumer's call site.
- **`Trellis.ServiceDefaults`** — `UseEntityFrameworkUnitOfWork<TContext>()` is now annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` because the underlying `Trellis.EntityFrameworkCore` package is intentionally non-AOT (EF Core requires reflection over entity types). The AOT analyzer now surfaces this seam at the consumer's call site; AOT consumers should compose their data access layer outside the builder.
- **README** — the unconditional "AOT-friendly" claim is softened to note that the composition-root convenience builder exposes both AOT-safe per-type overloads and assembly-scanning overloads with appropriate analyzer annotations.

### Added — `RequiredDateTimeOffset<TSelf>` and `Required*<T>` convenience attributes

- **`Trellis.Core`** — new `RequiredDateTimeOffset<TSelf>` primitive base class mirroring `RequiredDateTime<TSelf>` for instants whose originating UTC offset is part of the domain contract. Lenient by default (rejects `null` only); opt into `DateTimeOffset.MinValue` rejection via `[NotDefault]`. Generator support: `RequiredPartialClassGenerator` now recognises `RequiredDateTimeOffset` as a valid base, emits the full `TryCreate(DateTimeOffset)` / `TryCreate(DateTimeOffset?, string?)` / `TryCreate(string?, string?)` / `IFormattableScalarValue` factory family, and round-trips the offset through `IParsable<T>` / `ParsableJsonConverter<T>` via ISO 8601 round-trip ("O") format.
- **`Trellis.Core`** — four new numeric convenience attributes for `RequiredInt<TSelf>` / `RequiredLong<TSelf>` / `RequiredDecimal<TSelf>`: `[Positive]` (rejects `<= 0`), `[NonNegative]` (rejects `< 0`), `[Negative]` (rejects `>= 0`), `[NonPositive]` (rejects `> 0`). On `RequiredInt` and `RequiredLong` the generator synthesises the equivalent `[Range]` bounds; on `RequiredDecimal` it emits a direct sign comparison (the full decimal range exceeds what double-backed `[Range]` can express). New generator diagnostics: **TRLS043** — convenience attribute on a non-numeric Required base; **TRLS044** — more than one convenience attribute on the same class (the sign constraints are mutually exclusive); **TRLS045** — convenience attribute combined with an explicit `[Range]` on the same class (the combination would silently disable the convenience sign check). The diagnostic IDs are also mirrored on `Trellis.Analyzers.TrellisDiagnosticIds` for the consumer-facing analyzer surface.

### Fixed — `Maybe<T>` equality silent miss-query

- **`Trellis.EntityFrameworkCore`** — `c.Phone == Maybe.From(value)` previously translated to `_phone IS NULL` because EF Core funcletization extracted both `Maybe<T>.None` and `Maybe.From(value)` to `QueryParameterExpression` *before* `MaybeQueryInterceptor.QueryCompilationStarting` ran, erasing the syntactic difference. The fix is a new `MaybeEvaluatableExpressionFilterPlugin` registered via `MaybeEvaluatableExpressionFilterExtension` (an `IDbContextOptionsExtension`) that returns `false` for the three literal operand shapes — `Maybe<T>.None` static property access, `default(Maybe<T>)`, and `Maybe.From(value)` / `Maybe<T>.From(value)` calls — so they stay un-funcletized. `MaybeExpressionRewriter` now recognises each literal shape: `None` and `default(Maybe<T>)` translate to typed `null`; `Maybe.From(arg)` extracts `arg` and lifts it to the storage type, producing `_field = @p`. The plugin is wired automatically by every `AddTrellisInterceptors(...)` overload; consumers already calling `AddTrellisInterceptors(...)` get the fix with no registration change. Consumers registering the interceptor directly via `optionsBuilder.AddInterceptors(new MaybeQueryInterceptor())` must migrate to `AddTrellisInterceptors(...)` (or add the new options extension themselves) to pick up the equality fix.
- **`Trellis.EntityFrameworkCore`** — `MaybeExpressionRewriter` no longer silently converts unrecognised `Maybe<T>`-typed operands to typed null. A captured local of type `Maybe<T>` (for example `var m = Maybe.From(value); db.Where(c => c.Phone == m);`) now throws `InvalidOperationException` with an actionable message naming the supported alternatives (`Maybe.From(value)` inlined at the comparison site, or `MaybeQueryableExtensions.WhereEquals(...)`). This trades the historic silent miss-query for an explicit failure at translation time.

### Added — IETF `Idempotency-Key` middleware

- **`Trellis.Asp`** — new `Trellis.Asp.Idempotency` namespace shipping opt-in middleware that implements [`draft-ietf-httpapi-idempotency-key-header`](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/) for `POST` / `PATCH` retry safety. Endpoints opt in with `[Idempotent]` (or `.WithMetadata(new IdempotentAttribute())` in Minimal API); the middleware is a no-op on endpoints without it and on methods outside `IdempotencyOptions.Methods`. The middleware reads the configured header (default `Idempotency-Key`), parses it as the [RFC 8941](https://www.rfc-editor.org/rfc/rfc8941) `sf-string` subset, buffers the request body up to `MaxRequestBodyBytes` (default `1 MiB`), computes a SHA-256 fingerprint over `(method, path, normalized headers, body)`, resolves an isolation scope via `IIdempotencyScopeResolver` (default `DefaultIdempotencyScopeResolver` — per-actor when `IActorProvider` is registered, anonymous otherwise), and contracts with `IIdempotencyStore.TryReserveAsync(...)` for one of four outcomes: `Reserved(reservationId)` — the first request, the response is captured by a teeing `IHttpResponseBodyFeature` decorator and snapshotted on success; `AlreadyInFlight(retryAfter)` — `409 Conflict` with `Retry-After`; `Replay(snapshot)` — the captured status code, headers, and body are written verbatim; `BodyHashMismatch(storedFingerprint)` — `422 Unprocessable Entity`. Reservation tokens are opaque `string` GUIDs the store uses for CAS so a stale completer cannot finalise a reservation the sweeper already abandoned. Finalisation runs under a bounded 5-second cancellation token so `AbandonAsync` still executes when the client disconnects. New types: `IdempotentAttribute`, `IdempotencyOptions`, `IIdempotencyStore`, `IdempotencyReservationOutcome`, `IIdempotencyScopeResolver`, `DefaultIdempotencyScopeResolver`, `AnonymousIdempotencyScopeResolver`, `ActorIdempotencyScopeResolver`, `InMemoryIdempotencyStore`, `CapturingResponseBodyFeature`, `IdempotencyKeyParser`, `IdempotencyFingerprint`, `IdempotencyMiddleware`. New extensions: `services.AddTrellisIdempotency(configure?)`, `services.AddInMemoryIdempotencyStore()`, `app.UseTrellisIdempotency()`. The in-memory store is single-process only and intended for tests and dev hosts; production multi-instance hosts need an EF-backed store implementing the same CAS contract.
- **`Trellis.ServiceDefaults`** — new `TrellisServiceBuilder.UseIdempotency(Action<IdempotencyOptions>? configure = null)` slot, applied after `UseProblemDetails()` and before actor-provider registration so opted-in endpoints' scope resolver composes cleanly with `IActorProvider`. Independent of `UseAsp()`. Composition is explicit: the slot wires options + scope resolver + the marker `app.UseTrellisIdempotency()` checks at startup, but does not register a store — callers add `services.AddInMemoryIdempotencyStore()` (dev / tests) or an EF-backed store (production). Repeated calls compose the configure delegates rather than overwriting, mirroring `UseAsp` / `UseMediator`.

### Behavior — idempotency abandons non-replayable outcomes

- **`Trellis.Asp.Idempotency`** — the middleware now abandons the reservation (instead of caching a snapshot) when the handler returns a 5xx status code or when the response wrote trailers. 5xx responses are treated as transient per the IETF Idempotency-Key draft, so the next retry with the same key re-executes the handler instead of replaying a transient failure. Responses that wrote trailers (via `IHttpResponseTrailersFeature`) cannot be reproduced by the snapshot writer, so caching them would silently drop the trailers on replay; those reservations are abandoned and the next retry re-executes. New `ILogger` events at EventId 6 (trailers-abandoned) and EventId 7 (5xx-abandoned) make the abandon path visible to operators.

### Fixed — idempotency

- **`Trellis.Asp.Idempotency`** — `IdempotencyKeyParser` now rejects an unescaped `"` inside a quoted string with a position-aware diagnostic, matching RFC 8941's requirement that embedded quotes are escaped as `\"`. Previously, `"abc"junk"` was accepted as `abcjunk` because the parser appended the closing `"` to the string buffer instead of treating the second `"` as an end-of-string marker.
- **`Trellis.Asp.Idempotency`** — `IIdempotencyStore.TryReserveAsync` XML doc for the `fingerprint` parameter corrected from "hex digest" to "URL-safe base64 (no padding) digest" so store implementers do not size storage columns for the wrong encoding (`IdempotencyFingerprint.Compute` returns 43-character base64url, not 64-character hex).

### Added — idempotent inserts

- **`Trellis.EntityFrameworkCore`** — new `DbContextIdempotencyExtensions.TryInsertUniqueAsync<TEntity>(this DbContext, TEntity, CancellationToken)` helper that adds the entity, persists it, and converts a provider-level unique-constraint violation into `Result.Fail<TEntity>(new Error.Conflict(null, "duplicate.key") { Detail = "A record with the same unique value already exists.", ConstraintName, ConstraintTableName })`. The added entity is detached from the change tracker on the duplicate path so a caller retrying with a freshly-constructed entity does not re-flush the original. Foreign-key violations, `DbUpdateConcurrencyException`, connection-level exceptions, and `OperationCanceledException` propagate normally. The helper throws `InvalidOperationException` when the context already has pending changes on entry so a duplicate-key violation cannot be mis-attributed to the inserted entity.
- **`Trellis.EntityFrameworkCore`** — new `DbExceptionClassifier.ExtractConstraintIdentity(DbUpdateException)` returning `(string? ConstraintName, string? TableName)`. Typed extraction first for PostgreSQL (`Npgsql.PostgresException.ConstraintName` / `TableName` / `SchemaName` via reflection so the package stays provider-agnostic; output is `"schema.table"` when both are present); falls back to regex-based parsing for SQL Server errors 2627 / 2601 / FK 547, SQLite `UNIQUE constraint failed: <Table>.<Column>` / `PRIMARY KEY` (FK form returns null pair because SQLite does not name the constraint), and MySQL `Duplicate entry '...' for key '<table>.<key>'`. Defensive: returns `(null, null)` on any unexpected exception so telemetry extraction never breaks a caller.
- **`Trellis.Core`** — `Error.Conflict` gains two `[JsonIgnore]` init-only telemetry fields `ConstraintName` and `ConstraintTableName` (defaults `null`, preserves equality for existing call sites). The new fields are intended for structured logging and are never serialized to API responses; the safe-for-clients message stays in `Detail`.
- **`Trellis.EntityFrameworkCore`** — `SaveChangesResultAsync` and `SaveChangesWithRetryAsync` now populate `ConstraintName` / `ConstraintTableName` on the `Error.Conflict` they return for the `duplicate.key` and `referential.integrity` reason codes (no change to `retry.exhausted` / `retry.aborted` paths).

### Added — `Trellis.Testing.Worker`

- **`Trellis.Testing.Worker`** — new package shipping `WorkerHarness<TWorker>` for integration-testing `BackgroundService` workers. Builds an `IHost` with a deterministic `FakeTimeProvider`, a configurable `TestActorProvider`, and an open-generic capture handler wired into the mediator's `IDomainEventHandler<>` pipeline so tests can advance time, await the first domain event of a given type (with an optional predicate), or await a named tick signal via `IWorkerTickSignal`. The harness owns hosted-service registration for the worker under test and fails fast if `ConfigureServices` also calls `AddHostedService<TWorker>()`. Wait timeouts run on real time so `Time.Advance(...)` only drives the worker's `Task.Delay` / `PeriodicTimer` continuations. `AutoStart` defaults to `false` so tests can subscribe before the worker emits. `WaitForEventAsync` returns the matching `TEvent`; timeouts raise `WorkerHarnessTimeoutException` with a diagnostic that names the awaited condition, captured-event counts, and reminds the user to register `AddDomainEventDispatch()` if missing. Caller cancellation propagates as plain `OperationCanceledException` so tests can distinguish wait-expired from caller-cancelled.

### Added — retry classification

- **`Trellis.Core`** — transport-neutral retry classification over the closed `Error` catalog: `RetryClassification` enum (`Transient`, `Permanent`, `FailFast`) and the static `ErrorRetryExtensions` class with `error.Classify()`, `error.IsTransient()`, `error.IsPermanent()`, `error.IsFailFast()`, and `error.GetRetryAdvice()`. `Classify` is exhaustive over all 12 `Error` cases. `Error.Aggregate` uses max-severity semantics over its inners; `GetRetryAdvice` returns `null` for `Error.Aggregate` by design. `Error.TransportFault` defaults to `Permanent` because every transport-specific payload shipped today (`HttpError.MethodNotAllowed`, `NotAcceptable`, `PreconditionFailed`, `ContentTooLarge`, `UnsupportedMediaType`, `RangeNotSatisfiable`, `PreconditionRequired`) is a caller-side error; retryable transient transport outcomes (HTTP 429, HTTP 503) are mapped at the boundary to `Error.RateLimited` / `Error.Unavailable` and never reach `Error.TransportFault`. Replaces hand-rolled `gatewayResult.Error is Error.Unavailable or Error.RateLimited` switches in worker loops, message consumers, and outbound-gateway clients.

### Added — pagination ergonomics

- **`Trellis.Core`** — first-class cursor pagination primitives shared by every storage adapter and transport: `PageSize` (with `FromRequested` lenient parser and `TryCreate` strict parser), `Cursor` (opaque `readonly record struct`), `CursorCodec` (URL-safe base64 encode / `TryDecode<TKey>` returning `Result<TKey>` with `Error.InvalidInput.ForField(..., "cursor.malformed", ...)` on bad input), `Page<T>` (`Items`, `Next`, `Previous`, `RequestedLimit`, `AppliedLimit`, `WasCapped`, `DeliveredCount`), `Page<T>.Map<TOut>` (preserves cursors and limits across DTO projection), and `PageBuilder.FromOverFetch` (storage-agnostic over-fetch slicer for single-key and composite `(CreatedAt, Id)` seek). All AOT-friendly — no JSON, no reflection, no `Expression.Compile`.
- **`Trellis.EntityFrameworkCore`** — `PaginationQueryableExtensions.ToPageAsync<T, TKey>(this IQueryable<T>, PageSize, Cursor?, Expression<Func<T, TKey>>, string?, CancellationToken)`. The helper owns the `OrderBy(keySelector)`, the cursor decode (returns `Result.Fail<Page<T>>` with `cursor.malformed` on bad input — never throws), the seek `WHERE` predicate (`Expression.GreaterThan` for numeric / `DateTime` / `DateTimeOffset` keys; `IComparable<TKey>.CompareTo` for `Guid` and `string` keys), the `Take(Applied + 1)` over-fetch, and the slice via `PageBuilder.FromOverFetch`. The canonical EF query-handler shape collapses to one line: `await db.Orders.AsNoTracking().ToPageAsync(pageSize, cursor, o => o.Id.Value, "cursor", ct)`. Value-object Id projections (`c => c.Id.Value`) require `AddTrellisInterceptors()`. Single-key seek requires a stable, unique ascending key; a composite `(CreatedAt, Id)` overload is deferred to a follow-up release.
- **`Trellis.Core`** — `Maybe<T>.HasValueWhere(Func<T, bool>)` for fluent presence-and-predicate checks; `Money.Sum` extension methods over `IEnumerable<Money>` with single-currency validation.

### Added — ProblemDetails `Instance` synthesised from `ResourceRef`

- **`Trellis.Core`** — new `ResourceCollectionNameAttribute(string name)` for marking an aggregate type with a non-default URI collection name (for example `[ResourceCollectionName("people")]` on `Person`). The attribute exposes the value via the `Name` property and validates that it is a non-empty single URL path segment composed only of RFC 3986 `unreserved` characters (ASCII letters and digits plus `-`, `.`, `_`, `~`) at construction time so misconfiguration fails fast. The `IsSafePathSegment(string?)` predicate is exposed as a public static helper (null/empty input returns `false`) so other layers can validate user-provided segment names against the same rule.
- **`Trellis.Asp`** — `ResponseFailureWriter` now synthesises `ProblemDetails.Instance` from the failing `ResourceRef` when the request URL does not already identify the resource. For example, `POST /api/orders` with body `{ customerId: "abc-123" }` that fails with `Error.NotFound(ResourceRef.For("Customer", "abc-123"))` now emits `"instance": "/customers/abc-123"` (the failing resource URI) and preserves the original request URI under `Extensions["request"]`. Applies to every error case that carries a `ResourceRef`: `NotFound`, `Gone`, `Conflict?`, `Forbidden?`, `InvariantViolation?`, and `TransportFault(HttpError.PreconditionFailed)`. Synthesis is suppressed when the URL already identifies the resource (segment- and query-value-aware match against both the raw id and its percent-decoded form; `+` is treated as space in query values to match ASP.NET Core's form-encoding semantics). The aggregate envelope (`Error.Aggregate`) never promotes a child's `ResourceRef` because the envelope itself carries no resource identity.
- **`Trellis.Asp`** — new `TrellisAspOptions.SynthesizeProblemDetailsInstanceFromResourceRef` toggle (default `true`). Set to `false` to preserve the historical request-URL-only `Instance` behavior. The new shape is strictly more informative, so the toggle exists for backward-compat-strict callers only.
- **`Trellis.Asp`** — four new `IServiceCollection` extensions for overriding the default collection name (`{Type.ToLowerInvariant()}s`): `AddResourceCollectionName<T>(string)` and `AddResourceCollectionName(string resourceType, string collectionName)` are AOT- and trim-friendly; `AddResourceCollectionNames(Assembly)` and `AddResourceCollectionNames(params Assembly[])` scan the supplied assemblies for `[ResourceCollectionName]` (both marked `[RequiresUnreferencedCode]`). Lookups are case-insensitive. Registered overrides are emitted verbatim into the synthesised URI (the lowercase guarantee applies only to the naive plural fallback). Conflicting registrations (same type → different collection names) fail fast at registry activation; identical registrations coalesce silently.

### Breaking changes — `Trellis.Core.Error` union DDD realignment

The `Trellis.Core.Error` discriminated union no longer embeds HTTP/RFC transport vocabulary. The domain stays transport-neutral; HTTP-specific error types now live in a new `Trellis.Http.Abstractions` package and flow through `Result<T>` via the `Error.TransportFault(ITransportFault Fault)` envelope.

The closed union now has 12 cases: `InvalidInput`, `InvariantViolation`, `NotFound`, `Forbidden`, `Conflict`, `Gone`, `AuthenticationRequired`, `Unavailable`, `RateLimited`, `Unexpected`, `Aggregate`, `TransportFault`.

#### Migration table

| Old | New |
|---|---|
| `Error.BadRequest("X")` | `Error.InvalidInput.ForRule("X")` |
| `Error.BadRequest("X", pointer)` | `Error.InvalidInput.ForField(pointer, "X")` |
| `Error.BadRequest("X") { Detail = d }` | `Error.InvalidInput.ForRule("X", d)` |
| `Error.UnprocessableContent(fields, rules)` | `new Error.InvalidInput(fields, rules)` |
| `Error.UnprocessableContent.ForField/ForRule(...)` | `Error.InvalidInput.ForField/ForRule(...)` |
| `Error.Unauthorized()` | `Error.AuthenticationRequired()` (optional `Scheme`) |
| `Error.TooManyRequests()` | `Error.RateLimited()` (optional `RetryAdvice`) |
| `Error.ServiceUnavailable()` | `Error.Unavailable()` (optional `ReasonCode`, `RetryAdvice`) |
| `Error.InternalServerError(faultId)` | `new Error.Unexpected(reasonCode, faultId)` |
| `Error.NotImplemented("X")` | `new Error.Unexpected(FaultCodes.NotImplemented) { Detail = "Feature 'X' is not implemented." }` |
| `Error.MethodNotAllowed`, `NotAcceptable`, `UnsupportedMediaType`, `RangeNotSatisfiable`, `ContentTooLarge`, `PreconditionFailed`, `PreconditionRequired` | Removed from `Trellis.Core`. Use `new Error.TransportFault(new HttpError.X(...))` from `Trellis.Http.Abstractions`. |

#### New cases

- `Error.InvariantViolation(ReasonCode, ResourceRef?)` — global / multi-field business invariant violated outside the inbound-validation pipeline.
- `Error.Aggregate(EquatableArray<Error>)` — first-class envelope for multiple failures.
- `Error.TransportFault(ITransportFault)` — envelope for transport-specific failures (currently `HttpError.*`).

#### New transport-neutral type

- `RetryAdvice(TimeSpan? After = null, DateTimeOffset? At = null)` in `Trellis.Core` — replaces the HTTP-specific `RetryAfterValue` on retry-bearing error cases. `RetryAfterValue` still exists, but now lives in `Trellis.Http.Abstractions` and is used only at the HTTP boundary.

#### Kind-slug changes

Telemetry consumers that aggregate failures by `Error.Kind` need to update their slug sets:

| Old slug | New slug |
|---|---|
| `bad-request` | `invalid-input` (BadRequest folded into InvalidInput) |
| `unprocessable-content` | `invalid-input` |
| `unauthorized` | `authentication-required` |
| `too-many-requests` | `rate-limited` |
| `service-unavailable` | `unavailable` |
| `internal-server-error` | `unexpected` |
| `not-implemented` | `unexpected` (with `ReasonCode == FaultCodes.NotImplemented`) |

#### Wire format unchanged

The HTTP boundary (`Trellis.Asp.ResponseFailureWriter`) preserves the historical problem-details `kind` extension tokens (`unprocessable-content`, `unauthorized`, `too-many-requests`, `service-unavailable`, `internal-server-error`, `not-implemented`) verbatim. External HTTP API consumers parsing problem-details see no wire change. The top-level Problem Details `type` field continues to default to ASP.NET's status-code URL. RFC 9110, 9457, and 6585 compliance is unaffected.

#### New package

`Trellis.Http.Abstractions` — shared by `Trellis.Asp` (server) and `Trellis.Http` (client). Hosts the `HttpError.*` closed union and the HTTP supporting types previously in `Trellis.Core` (`PreconditionKind`, `AuthChallenge`, `RetryAfterValue`, `EntityTagValue`, `AggregateETagExtensions`, `RepresentationMetadata`, `WriteOutcome<T>`). Add `<PackageReference Include="Trellis.Http.Abstractions" .../>` only when boundary code references these types directly; `Trellis.Asp` and `Trellis.Http` bring it in transitively.

See [`MIGRATION_v3.md`](MIGRATION_v3.md#error-union-ddd-realignment) for code-level before/after examples.

### Fixed

- **`Trellis.EntityFrameworkCore`** — `ApplyTrellisConventions(...)` now includes the `Trellis.Authorization` assembly in its default scan set. After the v3 typed-`ActorId` change, an aggregate carrying a `CreatedByActorId : ActorId` audit field silently failed EF mapping because the convention previously only included `Trellis.Core` and `Trellis.Primitives` by default; consumers had to pass `typeof(ActorId).Assembly` explicitly to get the scalar converter. The default scan set now mirrors the `Trellis.Primitives` precedent so `ApplyTrellisConventions(typeof(MyDomainId).Assembly)` is sufficient. `Trellis.EntityFrameworkCore` gains a project reference on `Trellis.Authorization` — a lightweight dependency (its only reference is `Trellis.Core`) that ASP consumers already receive transitively via `Trellis.Asp`.

### Breaking changes — server-side byte-range emission removed

Trellis targets general business web services, not media servers. The server-side byte-range emission surface duplicated `Microsoft.AspNetCore.Http.Results.File(stream, enableRangeProcessing: true)` and added no Trellis-specific value, so it has been removed.

**Removed public API**

- `Trellis.Asp.PartialContentHttpResult` (Minimal API `IResult`)
- `Trellis.Asp.PartialContentResult` (MVC `ObjectResult`)
- `Trellis.Asp.RangeRequestEvaluator` and the `RangeOutcome` closed union (`FullRepresentation` / `PartialContent` / `NotSatisfiable`)
- `HttpResponseOptionsBuilder<T>.WithRange(Func<T, ContentRangeHeaderValue>)`, `WithRange(long, long, long)`, `WithAcceptRanges(string)`
- `RepresentationMetadata.AcceptRanges` and `RepresentationMetadata.Builder.SetAcceptRanges(string)` (every other `RepresentationMetadata` member is unchanged)
- The `Status206PartialContent` `ProducesResponseTypeMetadata` entry from `TrellisHttpResult<TDomain, TBody>.PopulateMetadata` (OpenAPI no longer advertises `206` for Trellis-mapped endpoints)

**Migration**

- For binary downloads with RFC 9110 §14 byte-range semantics, call `Microsoft.AspNetCore.Http.Results.File(stream, enableRangeProcessing: true)` directly — ASP.NET Core implements byte semantics natively.
- For advisory headers such as `Accept-Ranges: none`, write the header on `HttpContext.Response.Headers` from middleware or the endpoint handler.

**Preserved (client-side typed-error round-trip)**

`HttpError.RangeNotSatisfiable(long CompleteLength, string Unit = "bytes")` still exists in `Trellis.Http.Abstractions`. Inbound `416` responses on the HTTP client continue to surface as `Error.TransportFault(new HttpError.RangeNotSatisfiable(...))` with the upstream `Content-Range` length and unit preserved, and `Trellis.Asp.ResponseFailureWriter` still emits `416` plus `Content-Range: {Unit} */{CompleteLength}` when such a fault propagates up through a `Result` chain.

## [3.0.0]

The first GA release under the **Trellis** name. This release supersedes
the `FunctionalDdd` 2.x line; consumers upgrading from `FunctionalDdd 2.1`
should follow the [migration guide](docs/docfx_project/articles/migration.md)
for step-by-step instructions. The summary below describes what changed in
the move from FunctionalDdd 2.1 to Trellis 3.0.

### Project rename and package reorganization (breaking)

- The project is renamed from `FunctionalDdd` to `Trellis`. The root
  namespace, all package ids, and all repository URLs change accordingly
  (e.g., `FunctionalDdd.RailwayOrientedProgramming` becomes `Trellis.Core`).
- The five FunctionalDdd packages are consolidated and expanded into the
  Trellis package family:

  | FunctionalDdd 2.1 package        | Trellis 3.0 package                                  |
  |----------------------------------|------------------------------------------------------|
  | `RailwayOrientedProgramming`     | `Trellis.Core` (folded with DomainDrivenDesign)      |
  | `DomainDrivenDesign`             | `Trellis.Core`                                       |
  | `CommonValueObjects`             | `Trellis.Primitives`                                 |
  | `Asp`                            | `Trellis.Asp`                                        |
  | `FluentValidation`               | `Trellis.FluentValidation`                           |

### New packages

- `Trellis.Mediator` — Result-aware in-process mediator with a canonical
  pipeline (exception → tracing → logging → authorization → resource
  authorization → validation → transactional commit, outermost to innermost).
  Supports both reflection-based and AOT-friendly source-generated dispatch.
- `Trellis.Authorization` — typed `Actor` / `ActorId`, `IAuthorize`,
  `IAuthorizeResource<TResource>`, `IAuthorizeResourceVia<TOwner>`, and the
  ASP integration points (`IActorProvider`, `ClaimsActorProvider`,
  `EntraActorProvider`, `CachingActorProvider`).
- `Trellis.EntityFrameworkCore` — unit-of-work, transactional command
  behavior, `TrellisScalarConverter`, the composite value object EF
  convention, `[OwnedEntity]` attribute, and supporting analyzers.
- `Trellis.StateMachine` — declarative aggregate state machines with
  compile-time transition validation.
- `Trellis.Asp.ApiVersioning` — `WithVersionedRoute()` helper (chained after
  `CreatedAtRoute(...)`, `CreatedAtAction(...)`, or `WithLocation(...)`) and versioned-projection guard
  rails for `Asp.Versioning.Http`.
- `Trellis.ServiceDefaults` — single composition root (`AddTrellis(...)`)
  that wires every framework slot in the right order.
- `Trellis.Http` — typed HTTP-client primitives for outbound calls.
- `Trellis.Testing` and `Trellis.Testing.AspNetCore` — FluentAssertions
  extensions for `Result<T>` / `IResult`, problem-details assertions, and
  WebApplicationFactory helpers.
- `Trellis.Analyzers` — Roslyn analyzers covering Maybe / Result misuse,
  ValueObject derivation, EF / JSON converter wiring, etc. (`TRLS001` …
  `TRLS042+`).

### `Error` redesigned as a closed ADT (breaking)

`Error` is no longer an open class with public constructors and ad-hoc
factories. It is now a closed algebraic data type whose only inhabitants
are the documented kinds: `Validation` / `UnprocessableContent`, `NotFound`,
`Conflict`, `Forbidden`, `Unauthorized`, `InternalServerError`. Each kind
has a typed factory (e.g., `Error.NotFound(...)`, `Error.Conflict(...)`)
that surfaces the metadata the wire mapper needs (resource references,
problem-details fields).

Consumers porting from `FunctionalDdd.Error`'s open constructor / generic
factories should map each call site to one of the kind-specific factories.
The migration guide covers the mechanical replacements.

### `Result<T>` JSON safety net (breaking)

`Result<T>` (and the `IResult` / `IResult<T>` interfaces) now carry a
default `[JsonConverter]` that throws `NotSupportedException` on any direct
JSON serialize / deserialize attempt. Previously, returning a raw
`Result<T>` from a controller silently produced a public-state JSON dump
(for example, `{"IsSuccess": true, "IsFailure": false, "Error": null}` for
a success, with no success value) and still had no HTTP status-code mapping.
The new converter fires on the first request and names the canonical fix:
call `.ToHttpResponse()` (Trellis.Asp) or unwrap
via `Match` / `TryGetValue` before serialization. Option-registered
converters take precedence and let consumers opt back in for logging /
IPC / storage scenarios.

### `RequiredXxx<T>` POLA realignment (breaking)

The `RequiredXxx<T>` family now follows a single rule: **reject only null**.
Per-type "zero value" rejection (`""` for strings, `0` for numerics,
`Guid.Empty`, `DateTime.MinValue`) is opt-in via the new `[NotDefault]`
attribute. String trim is opt-in via `[Trim]`. This makes the family
uniform with `RequiredInt<T>(0)` — which has always succeeded — and matches
the Principle of Least Astonishment.

Validation order in the generated `TryCreate` is `null → [Trim] →
[NotDefault] → [StringLength] / [Range] → ValidateAdditional`. New
compile-time diagnostics `TRLS040` (`[NotDefault]` on `RequiredBool<T>`),
`TRLS041` (`[Trim]` on a non-`RequiredString`), and `TRLS042` (`[NotDefault]`
on `RequiredEnum<T>`) cover degenerate combinations.

The EF Core `TrellisScalarConverter` rehydrates via `TryCreate`, so
lenient-by-default types now accept persisted sentinel values
(`Guid.Empty`, `""`, `DateTime.MinValue`). Add `[NotDefault]` to any
`RequiredGuid` / `RequiredDateTime` used as an aggregate id or EF-mapped
property to preserve strict rehydration.

### Actor model

- `Actor` is an entity (identity-based equality on `ActorId`); equality
  is no longer a record-style structural compare over every field.
- `Actor.Id` is strongly typed as `ActorId : RequiredString<ActorId>`
  (`[Trim, NotDefault]`). The string-accepting constructors and `Create`
  overloads remain for authentication-boundary code (claim → actor) and
  wrap the raw value via `ActorId.Create` internally. JSON serialization
  emits the raw string for wire compatibility.
- `IActorProvider.GetCurrentActorAsync` returns `Task<Maybe<Actor>>`
  (breaking) — anonymous requests are an absence, not a thrown exception.
  `CachingActorProvider` caches both successes and synchronous failures
  from the inner provider.
- `ClaimsActorProvider` understands both short and long claim-name forms
  (including `PermissionsClaim` and `JwtBearer.MapInboundClaims`).

### Mediator, domain events, and resource authorization

- `Trellis.Mediator` introduces a Result-aware pipeline; `AddMediator` and
  the AOT-friendly source-generator path share the same canonical order.
- Domain event dispatch lands inside the unit-of-work commit so events
  publish only when the transaction succeeds.
- Resource authorization (`IAuthorizeResource<TResource>`,
  `IAuthorizeResourceVia<TOwner>`) checks ownership / permissions against
  the loaded resource exactly once per request, slotted immediately before
  the validation behavior.
- Unified validation: a single `ValidationBehavior` runs `IValidate.Validate`
  plus every `IMessageValidator<TMessage>` in DI. FluentValidation plugs in
  as one such validator (`AddTrellisFluentValidation()`) rather than as its
  own pipeline slot.

### EF Core integration

- Composite value object convention: `[OwnedEntity]`-decorated composite
  VOs flow through `TrellisCompositeValueObjectConvention` and a generated
  EF model configuration. The supported shape is `{ get; private set; }`
  on every property — `TRLS022` enforces this.
- `TrellisScalarConverter` round-trips `ScalarValueObject<TSelf, T>` via
  `TryCreate`, surfacing creation failures as
  `TrellisPersistenceMappingException`.
- `CompositeValueObjectJsonConverter<T>` and the matching analyzer
  (`TRLS020`) ensure DTOs exposing `[OwnedEntity]` composites carry a
  `[JsonConverter]` so STJ deserialization goes through `TryCreate`.
- New analyzer `TRLS021` flags redundant manual EF configuration that the
  convention already handles.

### ASP wire contract

- `Error.UnprocessableContent` is the canonical validation failure code;
  domain validation, binder-level value-object validation, and MVC body
  validation all return 422 with a problem-details payload that expands
  composite value-object failures into per-leaf entries.
- `WWW-Authenticate` is emitted on mediator-produced 401 responses.
- `ProblemDetails.Instance` is populated from the request URL (#496).
- `HttpResponseOptionsBuilder<T>` and `HttpResponseOptionsBuilder<Page<T>>`
  support `WithCacheControl(...)` / `CacheControl` presets, `VaryForActor()`
  / `IProvideActorVaryHeaders`, `WithETag` / `WithLastModified` /
  `Vary` / `WithContentLanguage` / `WithContentLocation`, and
  `EvaluatePreconditions()` (`If-None-Match` / `If-Modified-Since` → 304;
  failing `If-Match` / `If-Unmodified-Since` → 412), evaluated once per
  request so non-deterministic selectors do not produce inconsistent
  headers.
- `Maybe<TPrimitive>` is supported directly on DTOs (via
  `MaybePrimitiveJsonConverterFactory`) and on route / query / header
  parameters (via `MaybePrimitiveModelBinder<T>`).

### Documentation

- New cookbook (`docs/docfx_project/api_reference/trellis-api-cookbook.md`)
  with 26+ task-oriented recipes and a per-package API reference under
  `docs/docfx_project/api_reference/`.
- Value-object taxonomy reference
  (`trellis-value-object-taxonomy.md`) covering scalar / composite /
  primitive variants.
- Analyzer surface documented per rule under
  `docs/docfx_project/articles/analyzers/`.
- Migration guide (`docs/docfx_project/articles/migration.md`) covers the
  step-by-step move from FunctionalDdd 2.1 to Trellis 3.0.
- `Trellis.Core` and `Trellis.ServiceDefaults` README files describe the
  composition root and pipeline order.

---

## Previous Releases

Releases prior to 3.0.0 shipped under the FunctionalDdd project name and
are tracked in that repository's history.

[Unreleased]: https://github.com/xavierjohn/Trellis/compare/v3.0.0...HEAD
[3.0.0]: https://github.com/xavierjohn/Trellis/releases/tag/v3.0.0
