using FluentAssertions;
using Trellis;
using Trellis.Testing;
using Xunit;

namespace Trellis.Core.Tests.Results.Extensions.Await;

/// <summary>
/// Tests for T4-generated WhenAllAsync tuple overloads (WhenAllTs.g.tt).
/// These tests ensure at least one tuple permutation is covered to catch T4 generation bugs.
/// </summary>
public class WhenAllAsyncTupleTests : TestBase
{
    #region WhenAllAsync - Tuple Results

    [Theory]
    [MemberData(nameof(WhenAllAsyncResultScenarios))]
    public Task WhenAllAsync_ResultScenarios_BehaveAsExpected(string _, Func<Task> scenario) => scenario();

    [Fact]
    public async Task WhenAllAsync_Tuple2_FaultedTask_SurfacesException()
    {
        // Arrange
        var task1 = Task.FromException<Result<int>>(new InvalidOperationException("boom"));
        var task2 = Task.FromResult(Result.Ok("two"));

        // Act
        var act = () => (task1, task2).WhenAllAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");
    }

    #endregion

    #region WhenAllAsync - Chained with Bind

    [Fact]
    public async Task WhenAllAsync_Tuple3_ChainedWithBind_ProcessesTuple()
    {
        // Arrange
        var task1 = Task.FromResult(Result.Ok(10));
        var task2 = Task.FromResult(Result.Ok(20));
        var task3 = Task.FromResult(Result.Ok(30));

        // Act
        var result = await (task1, task2, task3)
            .WhenAllAsync()
            .BindAsync((a, b, c) => Result.Ok(a + b + c));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(60);
    }

    #endregion

    public static TheoryData<string, Func<Task>> WhenAllAsyncResultScenarios()
    {
        var data = new TheoryData<string, Func<Task>>();

        data.Add("tuple2 success", async () =>
        {
            var result = await (Task.FromResult(Result.Ok(1)), Task.FromResult(Result.Ok("two"))).WhenAllAsync();
            result.Should().BeSuccess().Which.Should().Be((1, "two"));
        });

        data.Add("tuple2 first failure", async () =>
        {
            var error = new Error.InternalServerError("test") { Detail = "error 1" };
            var result = await (Task.FromResult(Result.Fail<int>(error)), Task.FromResult(Result.Ok("two"))).WhenAllAsync();
            result.Should().BeFailure().Which.Should().Be(error);
        });

        data.Add("tuple2 second failure", async () =>
        {
            var error = new Error.InternalServerError("test") { Detail = "error 2" };
            var result = await (Task.FromResult(Result.Ok(1)), Task.FromResult(Result.Fail<string>(error))).WhenAllAsync();
            result.Should().BeFailure().Which.Should().Be(error);
        });

        data.Add("tuple2 both failures combine validation errors", async () =>
        {
            var error1 = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field1"), "validation.error") { Detail = "Error 1" }));
            var error2 = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field2"), "validation.error") { Detail = "Error 2" }));
            var result = await (Task.FromResult(Result.Fail<int>(error1)), Task.FromResult(Result.Fail<string>(error2))).WhenAllAsync();
            result.Should().BeFailureOfType<Error.UnprocessableContent>();
        });

        data.Add("tuple3 success", async () =>
        {
            var result = await (
                Task.FromResult(Result.Ok(1)),
                Task.FromResult(Result.Ok("two")),
                Task.FromResult(Result.Ok(3.0))).WhenAllAsync();
            result.Should().BeSuccess().Which.Should().Be((1, "two", 3.0));
        });

        data.Add("tuple3 one failure", async () =>
        {
            var error = new Error.InternalServerError("test") { Detail = "error 1" };
            var result = await (
                Task.FromResult(Result.Ok(1)),
                Task.FromResult(Result.Fail<string>(error)),
                Task.FromResult(Result.Ok(3.0))).WhenAllAsync();
            result.Should().BeFailure().Which.Should().Be(error);
        });

        data.Add("tuple4 success", async () =>
        {
            var result = await (
                Task.FromResult(Result.Ok(1)),
                Task.FromResult(Result.Ok(2)),
                Task.FromResult(Result.Ok(3)),
                Task.FromResult(Result.Ok(4))).WhenAllAsync();
            result.Should().BeSuccess().Which.Should().Be((1, 2, 3, 4));
        });

        data.Add("tuple4 last failure", async () =>
        {
            var error = new Error.InternalServerError("test") { Detail = "error 1" };
            var result = await (
                Task.FromResult(Result.Ok(1)),
                Task.FromResult(Result.Ok(2)),
                Task.FromResult(Result.Ok(3)),
                Task.FromResult(Result.Fail<int>(error))).WhenAllAsync();
            result.Should().BeFailure().Which.Should().Be(error);
        });

        data.Add("tuple5 success", async () =>
        {
            var result = await (
                Task.FromResult(Result.Ok(1)),
                Task.FromResult(Result.Ok(2)),
                Task.FromResult(Result.Ok(3)),
                Task.FromResult(Result.Ok(4)),
                Task.FromResult(Result.Ok(5))).WhenAllAsync();
            result.Should().BeSuccess().Which.Should().Be((1, 2, 3, 4, 5));
        });

        data.Add("tuple9 success", async () =>
        {
            var result = await (
                Task.FromResult(Result.Ok(1)),
                Task.FromResult(Result.Ok(2)),
                Task.FromResult(Result.Ok(3)),
                Task.FromResult(Result.Ok(4)),
                Task.FromResult(Result.Ok(5)),
                Task.FromResult(Result.Ok(6)),
                Task.FromResult(Result.Ok(7)),
                Task.FromResult(Result.Ok(8)),
                Task.FromResult(Result.Ok(9))).WhenAllAsync();
            result.Should().BeSuccess().Which.Should().Be((1, 2, 3, 4, 5, 6, 7, 8, 9));
        });

        data.Add("tuple9 one failure", async () =>
        {
            var error = new Error.InternalServerError("test") { Detail = "error 1" };
            var result = await (
                Task.FromResult(Result.Ok(1)),
                Task.FromResult(Result.Ok(2)),
                Task.FromResult(Result.Ok(3)),
                Task.FromResult(Result.Ok(4)),
                Task.FromResult(Result.Fail<int>(error)),
                Task.FromResult(Result.Ok(6)),
                Task.FromResult(Result.Ok(7)),
                Task.FromResult(Result.Ok(8)),
                Task.FromResult(Result.Ok(9))).WhenAllAsync();
            result.Should().BeFailure().Which.Should().Be(error);
        });

        return data;
    }
}