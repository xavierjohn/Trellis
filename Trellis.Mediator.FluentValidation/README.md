# Trellis.Mediator.FluentValidation

Mediator pipeline adapter that plugs [FluentValidation](https://github.com/FluentValidation/FluentValidation) validators into the `Trellis.Mediator` validation stage via the open-generic `IMessageValidator<TMessage>` extension point.

## Package boundary

This package exists to keep `Trellis.FluentValidation` Domain-layer-pure. `Trellis.FluentValidation` provides `ValidationResult` → `Result<T>` conversion (Domain) and the `JsonPointerNormalizer` helper (RFC 6901). `Trellis.Mediator.FluentValidation` provides the mediator-pipeline adapter (`FluentValidationMessageValidatorAdapter<TMessage>`) and its DI registration helpers (`AddTrellisFluentValidation`).

Reference from your **Application** or composition root project, not your Domain project.

## See

- The package-level [`NUGET_README.md`](NUGET_README.md) for a quick example.
- The cookbook ["Recipe 2 — Command + handler + FluentValidation + EF persistence"](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-cookbook.html#recipe-2--command--handler--fluentvalidation--ef-persistence) for the end-to-end pattern.
- The [`Trellis.Mediator.FluentValidation` API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-mediator-fluentvalidation.html) for the full surface.
