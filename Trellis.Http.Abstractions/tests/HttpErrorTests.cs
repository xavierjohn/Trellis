namespace Trellis.Http.Abstractions.Tests;

using System.Reflection;
using Trellis.Testing;

public sealed class HttpErrorTests
{
    public static TheoryData<HttpError, string, string> ConstructionCases => new()
    {
        { new HttpError.MethodNotAllowed(EquatableArray.Create("GET", "PUT")), "method-not-allowed", "method-not-allowed" },
        { new HttpError.NotAcceptable(EquatableArray.Create("application/json")), "not-acceptable", "not-acceptable" },
        { new HttpError.UnsupportedMediaType(EquatableArray.Create("application/json")), "unsupported-media-type", "unsupported-media-type" },
        { new HttpError.RangeNotSatisfiable(128), "range-not-satisfiable", "range-not-satisfiable" },
        { new HttpError.ContentTooLarge(1024), "content-too-large", "content-too-large" },
        { new HttpError.PreconditionFailed(ResourceRef.For("Widget", 42), PreconditionKind.IfMatch), "precondition-failed", "IfMatch" },
        { new HttpError.PreconditionRequired(PreconditionKind.IfUnmodifiedSince), "precondition-required", "IfUnmodifiedSince" },
    };

    [Theory]
    [MemberData(nameof(ConstructionCases))]
    public void Every_case_constructs_with_expected_kind_and_code(HttpError error, string kind, string code)
    {
        error.Kind.Should().Be(kind);
        error.Code.Should().Be(code);
    }

    [Fact]
    public void Equality_includes_payload_and_detail()
    {
        var left = new HttpError.MethodNotAllowed(EquatableArray.Create("GET"))
        {
            Detail = "PATCH is not supported.",
        };
        var right = new HttpError.MethodNotAllowed(EquatableArray.Create("GET"))
        {
            Detail = "PATCH is not supported.",
        };
        var differentPayload = left with { Allow = EquatableArray.Create("POST") };

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
        left.Should().NotBe(differentPayload);
        left.Should().NotBe(left with { Detail = "A different detail." });
    }

    [Fact]
    public void Cause_is_preserved_but_excluded_from_equality()
    {
        var cause = new HttpError.ContentTooLarge(512)
        {
            Detail = "Body too large.",
        };
        var left = new HttpError.UnsupportedMediaType(EquatableArray.Create("application/json"));
        var right = new HttpError.UnsupportedMediaType(EquatableArray.Create("application/json"))
        {
            Cause = cause,
        };

        right.Cause.Should().BeSameAs(cause);
        left.Should().Be(right);
    }

    [Fact]
    public void ToString_prefers_detail_when_present()
    {
        var error = new HttpError.PreconditionRequired(PreconditionKind.IfMatch)
        {
            Detail = "If-Match is required.",
        };

        error.ToString().Should().Be("precondition-required: If-Match is required.");
    }

    [Fact]
    public void Cause_chain_cycle_throws_invalid_operation_exception()
    {
        var cycle = new HttpError.MethodNotAllowed(EquatableArray.Create("GET"));
        var causeField = typeof(HttpError).GetField("_cause", BindingFlags.Instance | BindingFlags.NonPublic)!;
        causeField.SetValue(cycle, cycle);

        var act = () => _ = new HttpError.NotAcceptable(EquatableArray<string>.Empty)
        {
            Cause = cycle,
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("HttpError.Cause chain contains a cycle.");
    }
}