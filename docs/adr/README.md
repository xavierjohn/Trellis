# Architecture Decision Records

This folder records significant design decisions for the Trellis framework. An ADR captures
the *why* behind a non-obvious choice — including alternatives that were explored and rejected,
and the empirical evidence that drove the conclusion. ADRs are append-only: new decisions get
new files; superseded decisions stay in place with a status update.

## Index

| #  | Title | Status |
|----|-------|--------|
| 001 | [Result API Surface — `Value` / `Error` Accessors](ADR-001-result-api-surface.md) | Accepted (PR7) |

## When to write an ADR

Write a new ADR whenever:

- A design decision was non-obvious enough that a reasonable contributor might want to "fix" it later (i.e., the obvious-looking alternative was tried and rejected for non-obvious reasons).
- The decision affects public API surface, on-the-wire contracts, or framework-wide invariants.
- Multiple plausible alternatives exist and the choice between them required tradeoff analysis.

Document each variant tried, what made it look attractive, and the specific failure mode that ruled it out. Future readers should be able to short-circuit re-deriving the same conclusions.
