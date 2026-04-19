namespace Trellis.Showcase.Tests.Domain;

using Trellis;
using Trellis.Primitives;

/// <summary>
/// Demonstrates exhaustive pattern matching over the Error ADT.
/// The compiler enforces the cases at the discard arm — adding a new Error case is a
/// deliberate, breaking change for callers.
/// </summary>
public class ErrorMatchTests
{
    [Theory]
    [InlineData(typeof(Error.UnprocessableContent), "unprocessable")]
    [InlineData(typeof(Error.NotFound), "not-found")]
    [InlineData(typeof(Error.Conflict), "conflict")]
    [InlineData(typeof(Error.Forbidden), "forbidden")]
    [InlineData(typeof(Error.PreconditionFailed), "precondition")]
    [InlineData(typeof(Error.InternalServerError), "internal")]
    public void Match_returns_expected_label_for_each_case(Type errorType, string expectedLabel)
    {
        Error error = errorType.Name switch
        {
            nameof(Error.UnprocessableContent) => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty),
            nameof(Error.NotFound) => new Error.NotFound(new ResourceRef("Thing", "1")),
            nameof(Error.Conflict) => new Error.Conflict(null, "x"),
            nameof(Error.Forbidden) => new Error.Forbidden("policy.id"),
            nameof(Error.PreconditionFailed) => new Error.PreconditionFailed(new ResourceRef("Thing", "1"), PreconditionKind.IfMatch),
            nameof(Error.InternalServerError) => new Error.InternalServerError("fault-id"),
            _ => throw new InvalidOperationException(),
        };

        var label = Classify(error);
        label.Should().Be(expectedLabel);
    }

    private static string Classify(Error error) => error switch
    {
        Error.UnprocessableContent => "unprocessable",
        Error.NotFound => "not-found",
        Error.Conflict => "conflict",
        Error.Forbidden => "forbidden",
        Error.PreconditionFailed => "precondition",
        Error.InternalServerError => "internal",
        _ => "other",
    };
}
