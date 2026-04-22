namespace Trellis.Showcase.Application.Features.SubmitBatchTransfers;

using global::FluentValidation;

/// <summary>
/// FluentValidation rules for <see cref="SubmitBatchTransfersCommand"/>. Demonstrates the
/// two FluentValidation property-name shapes that JSON-Pointer normalization handles:
/// <list type="bullet">
///   <item><description><c>RuleFor(c =&gt; c.Metadata.Reference)</c> produces the FluentValidation
///   property name <c>"Metadata.Reference"</c>, which the Trellis adapter translates to
///   <c>/Metadata/Reference</c>.</description></item>
///   <item><description><c>RuleForEach(c =&gt; c.Lines).ChildRules(...)</c> produces names like
///   <c>"Lines[0].Memo"</c>, which the adapter translates to <c>/Lines/0/Memo</c>.</description></item>
/// </list>
/// </summary>
public sealed class SubmitBatchTransfersValidator : AbstractValidator<SubmitBatchTransfersCommand>
{
    public SubmitBatchTransfersValidator()
    {
        RuleFor(c => c.Metadata.Reference)
            .NotEmpty().WithMessage("Reference is required.")
            .Matches(@"^BATCH-\d{4}-\d{3}$").WithMessage("Reference must match pattern BATCH-YYYY-NNN.");

        RuleFor(c => c.Metadata.Description)
            .MaximumLength(500).WithMessage("Description must be at most 500 characters.");

        RuleForEach(c => c.Lines).ChildRules(line =>
            line.RuleFor(l => l.Memo)
                .NotEmpty().WithMessage("Memo is required.")
                .MaximumLength(200).WithMessage("Memo must be at most 200 characters."));
    }
}
