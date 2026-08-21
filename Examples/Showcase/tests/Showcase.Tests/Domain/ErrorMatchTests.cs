namespace Trellis.Showcase.Tests.Domain;

using Trellis;
using Trellis.Primitives;

/// <summary>
/// Demonstrates pattern matching over the Error ADT. This switch handles the cases the
/// sample maps to HTTP responses; the discard arm throws to surface any unhandled case
/// loudly during testing if a new <see cref="Error"/> case is introduced upstream.
/// </summary>
public class ErrorMatchTests
{
    [Theory]
    [InlineData(typeof(Error.InvalidInput), "invalid")]
    [InlineData(typeof(Error.NotFound), "not-found")]
    [InlineData(typeof(Error.Conflict), "conflict")]
    [InlineData(typeof(Error.Forbidden), "forbidden")]
    [InlineData(typeof(Error.TransportFault), "precondition")]
    [InlineData(typeof(Error.Unexpected), "internal")]
    public void Match_returns_expected_label_for_each_case(Type errorType, string expectedLabel)
    {
        Error error = errorType.Name switch
        {
            nameof(Error.InvalidInput) => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty),
            nameof(Error.NotFound) => new Error.NotFound(new ResourceRef("Thing", "1")),
            nameof(Error.Conflict) => new Error.Conflict(null, "x"),
            nameof(Error.Forbidden) => new Error.Forbidden("policy.id"),
            nameof(Error.TransportFault) => new Error.TransportFault(new HttpError.PreconditionFailed(new ResourceRef("Thing", "1"), PreconditionKind.IfMatch)),
            nameof(Error.Unexpected) => new Error.Unexpected("test.reason", "fault-id"),
            _ => throw new InvalidOperationException(),
        };

        var label = Classify(error);
        label.Should().Be(expectedLabel);
    }

    private static string Classify(Error error) => error switch
    {
        Error.InvalidInput => "invalid",
        Error.NotFound => "not-found",
        Error.Conflict => "conflict",
        Error.Forbidden => "forbidden",
        Error.TransportFault { Fault: HttpError.PreconditionFailed } => "precondition",
        Error.Unexpected => "internal",
        _ => throw new InvalidOperationException($"Unhandled Error case: {error.GetType().Name}"),
    };
}