using FluentAssertions;
using Trellis;
using Xunit;

namespace Trellis.Core.Tests.Results.Extensions;

using Trellis.Testing;

public class TryTests
{
    [Fact]
    public void Try_wraps_exception_into_failure()
    {
        var r = Result.Try<int>(() => throw new InvalidOperationException("Boom"));

        r.IsFailure.Should().BeTrue();
        var error = r.Error.Should().BeOfType<Error.Unexpected>().Subject;
        error.ReasonCode.Should().Be("unhandled-exception");
        error.FaultId.Should().NotBeNullOrWhiteSpace();
        error.Detail.Should().Be("An unexpected error occurred while processing the request.");
        error.Detail.Should().NotContain("Boom");
    }

    [Fact]
    public void Try_returns_success_on_normal_execution()
    {
        var r = Result.Try(() => 123);

        r.IsSuccess.Should().BeTrue();
        r.Unwrap().Should().Be(123);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2201:Do not raise reserved exception types")]
    public async Task TryAsync_wraps_exception()
    {
        var r = await Result.TryAsync<int>(async () =>
        {
            await Task.Delay(5);
            throw new Exception("AsyncBoom");
        });

        r.IsFailure.Should().BeTrue();
        var error = r.Error.Should().BeOfType<Error.Unexpected>().Subject;
        error.ReasonCode.Should().Be("unhandled-exception");
        error.FaultId.Should().NotBeNullOrWhiteSpace();
        error.Detail.Should().Be("An unexpected error occurred while processing the request.");
        error.Detail.Should().NotContain("AsyncBoom");
    }

    [Fact]
    public async Task TryAsync_success()
    {
        var r = await Result.TryAsync(async () =>
        {
            await Task.Delay(5);
            return 7;
        });

        r.IsSuccess.Should().BeTrue();
        r.Unwrap().Should().Be(7);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2201:Do not raise reserved exception types")]
    public void Custom_exception_mapper()
    {
        var r = Result.Try<int>(() => throw new Exception("HideMe"), ex => Error.InvalidInput.ForRule("bad.request", "Mapped"));

        r.IsFailure.Should().BeTrue();
        r.Error!.Should().Be(Error.InvalidInput.ForRule("bad.request", "Mapped"));
    }
}