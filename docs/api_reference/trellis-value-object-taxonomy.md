# Trellis Value Object Taxonomy

## Purpose

This memo proposes a cleaner taxonomy for Trellis value objects so new types are designed against explicit categories instead of being forced into the nearest existing pattern.

The RequiredEnum issue showed that local correctness is not enough. Trellis needs category-level consistency checks:

1. What is the semantic identity of the type?
2. What is the canonical creation path?
3. What is the canonical wire/storage shape?
4. Is any public API exposing infrastructure details instead of domain meaning?
5. Do generators, converters, and conventions work without special casing?

## Proposed Categories

### 1. Scalar Value Objects

Definition: A type whose semantic identity is exactly one primitive value.

Examples:

- `EmailAddress`
- `PhoneNumber`
- `OrderId`
- `FirstName`
- `RequiredString<TSelf>` derivatives
- `RequiredGuid<TSelf>` derivatives
- `RequiredInt<TSelf>` derivatives
- `RequiredDecimal<TSelf>` derivatives

Required properties:

- One canonical public scalar: `Value`
- One canonical factory pattern: `TryCreate(...)` and `Create(...)`
- Converters and EF mapping use `Value` and `Create(...)` without special cases

Design rule:

If a type is in this category, `Value` must be the true semantic scalar, not a convenience property or implementation detail.

### 2. Symbolic Value Objects

Definition: A type whose semantic identity is one named member from a finite set, often with behavior.

Examples:

- `RequiredEnum<TSelf>`
- Order state machines
- Role or status concepts with behavior

Required properties:

- One canonical semantic identity, exposed through one public representation
- One canonical creation path matching that representation
- Stable contract rules for serialization and persistence

Design rule:

A symbolic value object must not expose both a semantic identity and a second public alias for the same concept. If the semantic identity is the string code, that code is the public contract. Incidental ordering should not be treated as identity.

Recommendation:

- Treat `Value` as the symbolic public name
- Default that name to the field name so there is one obvious source of truth
- Use an explicit override only when the external name must differ from the CLR field name
- Treat declaration order as non-semantic metadata, not primary identity

### 3. Structured Value Objects

Definition: A value object composed of multiple semantically meaningful components.

Examples:

- `Money`
- Future types like `DateRange`, `GeoLocation`, or `QuantityWithUnit`

Required properties:

- No requirement for a single `Value`
- Explicit JSON representation
- Explicit persistence strategy
- Clear equality semantics over components

Design rule:

Structured value objects are not failed scalar value objects. They are a separate category and should not be forced through scalar abstractions or infrastructure.

Recommendation:

- Document them as structured VOs
- Give them dedicated converter and persistence patterns
- Avoid implying they should behave like `IScalarValue<TSelf, TPrimitive>`

### 4. Optionality Wrappers

Definition: A wrapper that expresses absence/presence, not a domain scalar by itself.

Examples:

- `Maybe<T>`

Required properties:

- Must compose cleanly with the wrapped type
- Must not force application code to know storage implementation details
- Infrastructure integration should preserve the public property model whenever possible

Design rule:

Optionality wrappers are not value objects in the same sense as scalars or structured VOs. They are container abstractions and should be reviewed as such.

Recommendation:

- Keep the domain API centered on the public `Maybe<T>` property
- Treat backing fields and generated members as hidden infrastructure details
- Prefer strongly typed infrastructure helpers over naming conventions leaking into app code

## Review Checklist

Every new Trellis value-like type should pass this checklist before it is considered stable:

1. Does the type clearly belong to one category?
2. Is there exactly one canonical semantic identity?
3. Is the creation API aligned with that identity?
4. Would a rename of a field or property accidentally change wire or storage contracts?
5. Can JSON, ASP.NET, analyzers, and EF handle the type without type-specific hacks?
6. Are any public properties exposing infrastructure concerns rather than domain meaning?

If the answer to item 5 is no, the first question should be whether the type is in the wrong category.

## Immediate Implications for Trellis

### RequiredEnum

`RequiredEnum<TSelf>` should be treated as a symbolic value object, not as a slightly unusual scalar.

That means:

- one canonical public symbolic identity
- no duplicate aliases for the same concept
- careful review of whether field-name-derived contracts are acceptable long term
- `Ordinal` should remain clearly non-semantic

### Money

`Money` should be treated as a structured value object.

That means:

- its special JSON and EF behavior is expected, not suspicious by itself
- the framework should document structured VO rules explicitly instead of implying every VO should look scalar

### Maybe<T>

`Maybe<T>` should be treated as an optionality wrapper.

That means:

- EF integration should aim to preserve the public property mental model
- internal backing-field conventions should not become part of application-level knowledge unless unavoidable

## Working Rule

Trellis should stop asking, "Can this be made to fit the scalar pipeline?"

It should instead ask, "What category is this type, and what infrastructure should that category get by default?"

That shift would have made the RequiredEnum flaw easier to catch before it spread into generators, converters, and documentation.

## Redesign Outcome

The redesign work driven by this taxonomy is complete.

### RequiredEnum

- `RequiredEnum<TSelf>` is now treated as a symbolic value object rather than a disguised scalar
- symbolic identity is centered on `Value`, with `EnumValueAttribute` available only when the external symbolic name must differ from the field name
- duplicate symbolic values now fail fast
- EF and converter infrastructure is category-driven, using symbolic lookup semantics instead of enum-specific special cases
- `Ordinal` remains public only as secondary declaration-order metadata and is no longer presented as a primary contract in docs or samples

### Maybe<T>

- EF guidance now stays property-first instead of teaching backing-field details as the normal programming model
- strongly typed helpers cover querying, indexing, bulk updates, diagnostics, and relationship-backed integration tests
- persistence diagnostics are explicit when invalid stored values cannot be materialized back into Trellis value objects

### Money

- `Money` is now documented consistently as a structured value object, not a scalar wrapper
- its JSON and EF owned-type behavior are treated as the expected structured-value-object path
- the public docs now distinguish built-in scalar primitives from structured `Money`

### Scalar Audit

- the built-in scalar primitive set was verified against the scalar contract:
	- `EmailAddress`
	- `PhoneNumber`
	- `Url`
	- `Slug`
	- `CurrencyCode`
	- `CountryCode`
	- `LanguageCode`
	- `IpAddress`
	- `Hostname`
	- `Age`
	- `Percentage`
- an executable regression test now locks that distinction in place so `Money` cannot drift into the scalar pipeline and the scalar set cannot drift out of it silently

### Review Rule

- the category checklist now lives in contributor guidance so new value-like types are reviewed as scalar, symbolic, structured, or optionality types before infrastructure is added around them

## Current Rule of Thumb

1. Use scalar abstractions only when the semantic identity is exactly one primitive value.
2. Use symbolic value objects when the identity is one stable member from a finite set.
3. Use structured value objects when multiple components are semantically meaningful.
4. Keep optionality wrappers focused on the wrapped property model, not the persistence workaround.