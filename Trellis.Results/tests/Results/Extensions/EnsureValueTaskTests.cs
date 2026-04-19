namespace Trellis.Results.Tests.Results.Extensions;

using System.Threading.Tasks;
using FluentAssertions;
using Trellis;
using Xunit;

public class EnsureValueTaskTests
{
    [Fact]
    public async Task Ensure_ValueTaskRight_short_circuits_when_source_is_failure()
    {
        var source = Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "original" });
        var invoked = false;

        var actual = await source.EnsureAsync(
            () =>
            {
                invoked = true;
                return new ValueTask<bool>(true);
            },
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "should not run" });

        invoked.Should().BeFalse();
        actual.Should().Be(source);
    }

    [Fact]
    public async Task Ensure_ValueTaskRight_handles_boolean_predicate()
    {
        var source = Result.Ok("payload");
        var error = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "predicate failed" };

        var success = await source.EnsureAsync(() => new ValueTask<bool>(true), error);
        success.Should().Be(source);

        var failure = await source.EnsureAsync(() => new ValueTask<bool>(false), error);
        failure.IsFailure.Should().BeTrue();
        failure.Error!.Should().Be(error);
    }

    [Fact]
    public async Task Ensure_ValueTaskRight_uses_value_based_predicates()
    {
        var source = Result.Ok("abc");
        var error = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "length invalid" };

        var passing = await source.EnsureAsync(v => new ValueTask<bool>(v.Length == 3), error);
        passing.IsSuccess.Should().BeTrue();

        var failing = await source.EnsureAsync(v => new ValueTask<bool>(v.Length > 5), error);
        failing.Error!.Should().Be(error);
    }

    [Fact]
    public async Task Ensure_ValueTaskRight_invokes_error_factories()
    {
        var source = Result.Ok("xyz");

        var errorFromSyncFactory = await source.EnsureAsync(
            v => new ValueTask<bool>(false),
            v => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"bad:{v}" });
        errorFromSyncFactory.Error!.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "bad:xyz" });

        var errorFromAsyncFactory = await source.EnsureAsync(
            v => new ValueTask<bool>(false),
            v => new ValueTask<Error>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"async:{v}" }));
        errorFromAsyncFactory.Error!.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "async:xyz" });
    }

    [Fact]
    public async Task Ensure_ValueTaskRight_supports_result_predicates()
    {
        var source = Result.Ok("data");
        var expected = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "predicate failure" };

        var predicateFailure = await source.EnsureAsync(
            () => new ValueTask<Result<string>>(Result.Fail<string>(expected)));
        predicateFailure.Error!.Should().Be(expected);

        var valuePredicateFailure = await source.EnsureAsync(
            value => new ValueTask<Result<string>>(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"bad:{value}" })));
        valuePredicateFailure.Error!.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "bad:data" });
    }

    #region ValueTask.Left - Func<bool> predicate with Error

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_bool_predicate_returns_success_when_predicate_passes()
    {
        var source = new ValueTask<Result<string>>(Result.Ok("test"));

        var actual = await source.EnsureAsync(() => true, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "should not fail" });

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Should().Be("test");
    }

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_bool_predicate_returns_failure_when_predicate_fails()
    {
        var source = new ValueTask<Result<string>>(Result.Ok("test"));
        var error = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "predicate failed" };

        var actual = await source.EnsureAsync(() => false, error);

        actual.IsFailure.Should().BeTrue();
        actual.Error!.Should().Be(error);
    }

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_bool_predicate_returns_original_failure()
    {
        var originalError = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "original failure" };
        var source = new ValueTask<Result<string>>(Result.Fail<string>(originalError));

        var actual = await source.EnsureAsync(() => true, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "should not be used" });

        actual.IsFailure.Should().BeTrue();
        actual.Error!.Should().Be(originalError);
    }

    #endregion

    #region ValueTask.Left - Func<TOk, bool> predicate with Error

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_value_predicate_returns_success_when_predicate_passes()
    {
        var source = new ValueTask<Result<string>>(Result.Ok("test"));

        var actual = await source.EnsureAsync(x => x.Length == 4, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "wrong length" });

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Should().Be("test");
    }

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_value_predicate_returns_failure_when_predicate_fails()
    {
        var source = new ValueTask<Result<string>>(Result.Ok("test"));
        var error = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "wrong length" };

        var actual = await source.EnsureAsync(x => x.Length > 10, error);

        actual.IsFailure.Should().BeTrue();
        actual.Error!.Should().Be(error);
    }

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_value_predicate_returns_original_failure()
    {
        var originalError = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "original failure" };
        var source = new ValueTask<Result<string>>(Result.Fail<string>(originalError));

        var actual = await source.EnsureAsync(x => x.Length == 4, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "should not be used" });

        actual.IsFailure.Should().BeTrue();
        actual.Error!.Should().Be(originalError);
    }

    #endregion

    #region ValueTask.Left - Func<TOk, bool> predicate with Func<TOk, Error> errorPredicate

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_sync_error_predicate_returns_success_when_predicate_passes()
    {
        var source = new ValueTask<Result<string>>(Result.Ok("test"));

        var actual = await source.EnsureAsync(
            x => x.Length == 4,
            x => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"expected 4 but got {x.Length}" });

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Should().Be("test");
    }

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_sync_error_predicate_returns_failure_when_predicate_fails()
    {
        var source = new ValueTask<Result<string>>(Result.Ok("test"));

        var actual = await source.EnsureAsync(
            x => x.Length > 10,
            x => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"length {x.Length} is too short" });

        actual.IsFailure.Should().BeTrue();
        actual.Error!.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "length 4 is too short" });
    }

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_sync_error_predicate_returns_original_failure()
    {
        var originalError = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "original failure" };
        var source = new ValueTask<Result<string>>(Result.Fail<string>(originalError));
        bool errorPredicateCalled = false;

        var actual = await source.EnsureAsync(
            x => x.Length == 4,
            x =>
            {
                errorPredicateCalled = true;
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "should not be called" };
            });

        actual.IsFailure.Should().BeTrue();
        actual.Error!.Should().Be(originalError);
        errorPredicateCalled.Should().BeFalse("error predicate should not be called when result is already a failure");
    }

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_sync_error_predicate_does_not_call_error_predicate_when_predicate_passes()
    {
        var source = new ValueTask<Result<string>>(Result.Ok("test"));
        bool errorPredicateCalled = false;

        var actual = await source.EnsureAsync(
            x => x.Length == 4,
            x =>
            {
                errorPredicateCalled = true;
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "should not be called" };
            });

        actual.IsSuccess.Should().BeTrue();
        errorPredicateCalled.Should().BeFalse("error predicate should not be called when predicate passes");
    }

    #endregion

    #region ValueTask.Left - Func<TOk, bool> predicate with Func<TOk, ValueTask<Error>> errorPredicate

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_async_error_predicate_returns_success_when_predicate_passes()
    {
        var source = new ValueTask<Result<string>>(Result.Ok("test"));
        bool errorPredicateCalled = false;

        var actual = await source.EnsureAsync(
            x => x.Length == 4,
            async x =>
            {
                errorPredicateCalled = true;
                await Task.Yield();
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "should not be called" };
            });

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Should().Be("test");
        errorPredicateCalled.Should().BeFalse("error predicate should not be called when predicate passes");
    }

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_async_error_predicate_returns_failure_when_predicate_fails()
    {
        var source = new ValueTask<Result<string>>(Result.Ok("test"));

        var actual = await source.EnsureAsync(
            x => x.Length > 10,
            async x =>
            {
                await Task.Yield();
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"length {x.Length} is too short" };
            });

        actual.IsFailure.Should().BeTrue();
        actual.Error!.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "length 4 is too short" });
    }

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_async_error_predicate_returns_original_failure()
    {
        var originalError = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "original failure" };
        var source = new ValueTask<Result<string>>(Result.Fail<string>(originalError));
        bool predicateCalled = false;
        bool errorPredicateCalled = false;

        var actual = await source.EnsureAsync(
            x =>
            {
                predicateCalled = true;
                return x.Length == 4;
            },
            async x =>
            {
                errorPredicateCalled = true;
                await Task.Yield();
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "should not be called" };
            });

        actual.IsFailure.Should().BeTrue();
        actual.Error!.Should().Be(originalError);
        predicateCalled.Should().BeFalse("predicate should not be called when result is already a failure");
        errorPredicateCalled.Should().BeFalse("error predicate should not be called when result is already a failure");
    }

    #endregion

    #region ValueTask.Left - Func<Result<TOk>> predicate

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_result_predicate_returns_success_when_predicate_succeeds()
    {
        var source = new ValueTask<Result<string>>(Result.Ok("test"));

        var actual = await source.EnsureAsync(() => Result.Ok<string>("ignored"));

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Should().Be("test");
    }

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_result_predicate_returns_failure_when_predicate_fails()
    {
        var source = new ValueTask<Result<string>>(Result.Ok("test"));
        var predicateError = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "predicate failed" };

        var actual = await source.EnsureAsync(() => Result.Fail<string>(predicateError));

        actual.IsFailure.Should().BeTrue();
        actual.Error!.Should().Be(predicateError);
    }

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_result_predicate_returns_original_failure()
    {
        var originalError = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "original failure" };
        var source = new ValueTask<Result<string>>(Result.Fail<string>(originalError));

        var actual = await source.EnsureAsync(() => Result.Ok<string>("should not be used"));

        actual.IsFailure.Should().BeTrue();
        actual.Error!.Should().Be(originalError);
    }

    #endregion

    #region ValueTask.Left - Func<TOk, Result<TOk>> predicate

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_value_result_predicate_returns_success_when_predicate_succeeds()
    {
        var source = new ValueTask<Result<string>>(Result.Ok("test"));

        var actual = await source.EnsureAsync(x => Result.Ok<string>("ignored"));

        actual.IsSuccess.Should().BeTrue();
        actual.Value.Should().Be("test");
    }

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_value_result_predicate_returns_failure_when_predicate_fails()
    {
        var source = new ValueTask<Result<string>>(Result.Ok("test"));

        var actual = await source.EnsureAsync(x => Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"{x} is invalid" }));

        actual.IsFailure.Should().BeTrue();
        actual.Error!.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "test is invalid" });
    }

    [Fact]
    public async Task Ensure_ValueTaskLeft_with_value_result_predicate_returns_original_failure()
    {
        var originalError = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "original failure" };
        var source = new ValueTask<Result<string>>(Result.Fail<string>(originalError));

        var actual = await source.EnsureAsync(x => Result.Ok<string>("should not be used"));

        actual.IsFailure.Should().BeTrue();
        actual.Error!.Should().Be(originalError);
    }

    #endregion
}