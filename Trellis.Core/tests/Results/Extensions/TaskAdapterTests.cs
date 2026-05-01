namespace Trellis.Core.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for public task adapters that wrap already-computed Result values.
/// </summary>
public class TaskAdapterTests
{
    [Fact]
    public async Task AsTask_GenericSuccess_PreservesSuccess()
    {
        var result = await Result.Ok(42).AsTask();

        result.Should().BeSuccess()
            .Which.Should().Be(42);
    }

    [Fact]
    public async Task AsTask_GenericFailure_PreservesFailure()
    {
        var error = new Error.Forbidden("orders.read");

        var result = await Result.Fail<int>(error).AsTask();

        result.Should().BeFailure()
            .Which.Should().Be(error);
    }

    [Fact]
    public async Task AsTask_GenericDefault_PreservesSentinelFailure()
    {
        var result = await default(Result<int>).AsTask();

        result.Should().BeFailureOfType<Error.Unexpected>()
            .Which.ReasonCode.Should().Be("default_initialized");
    }

    [Fact]
    public async Task AsTask_NonGenericSuccess_PreservesSuccess()
    {
        var result = await Result.Ok().AsTask();

        result.Should().BeSuccess();
    }

    [Fact]
    public async Task AsTask_NonGenericFailure_PreservesFailure()
    {
        var error = new Error.Forbidden("orders.read");

        var result = await Result.Fail(error).AsTask();

        result.Should().BeFailure()
            .Which.Should().Be(error);
    }

    [Fact]
    public async Task AsTask_NonGenericDefault_PreservesSentinelFailure()
    {
        var result = await default(Result<Unit>).AsTask();

        result.Should().BeFailureOfType<Error.Unexpected>()
            .Which.ReasonCode.Should().Be("default_initialized");
    }

    [Fact]
    public async Task AsValueTask_GenericSuccess_PreservesSuccess()
    {
        var result = await Result.Ok(42).AsValueTask();

        result.Should().BeSuccess()
            .Which.Should().Be(42);
    }

    [Fact]
    public async Task AsValueTask_GenericFailure_PreservesFailure()
    {
        var error = new Error.Forbidden("orders.read");

        var result = await Result.Fail<int>(error).AsValueTask();

        result.Should().BeFailure()
            .Which.Should().Be(error);
    }

    [Fact]
    public async Task AsValueTask_GenericDefault_PreservesSentinelFailure()
    {
        var result = await default(Result<int>).AsValueTask();

        result.Should().BeFailureOfType<Error.Unexpected>()
            .Which.ReasonCode.Should().Be("default_initialized");
    }

    [Fact]
    public async Task AsValueTask_NonGenericSuccess_PreservesSuccess()
    {
        var result = await Result.Ok().AsValueTask();

        result.Should().BeSuccess();
    }

    [Fact]
    public async Task AsValueTask_NonGenericFailure_PreservesFailure()
    {
        var error = new Error.Forbidden("orders.read");

        var result = await Result.Fail(error).AsValueTask();

        result.Should().BeFailure()
            .Which.Should().Be(error);
    }

    [Fact]
    public async Task AsValueTask_NonGenericDefault_PreservesSentinelFailure()
    {
        var result = await default(Result<Unit>).AsValueTask();

        result.Should().BeFailureOfType<Error.Unexpected>()
            .Which.ReasonCode.Should().Be("default_initialized");
    }
}