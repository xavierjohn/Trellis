namespace Trellis.Core.Tests.Results.Extensions;

using Trellis.Testing;

public class EnsureAllFactoryTests
{
    [Fact]
    public void EnsureAll_Factories_PassingChecks_DoNotCreateErrors()
    {
        var calls = 0;

        var result = Result.Ok("valid").EnsureAll(
            (value => value.Length > 0, _ => { calls++; return new Error.Unexpected("unused"); }
        ));

        result.Should().BeSuccess().Which.Should().Be("valid");
        calls.Should().Be(0);
    }

    [Fact]
    public void EnsureAll_Factories_FailedChecks_AccumulateValueDependentErrorsInOrder()
    {
        var values = new List<string>();

        var result = Result.Ok("invalid").EnsureAll(
            (_ => false, value =>
            {
                values.Add(value);
                return Error.InvalidInput.ForField(field: "first", code: "first.invalid", detail: value);
            }
        ),
            (_ => true, _ => new Error.Unexpected("unused")),
            (_ => false, value =>
            {
                values.Add(value);
                return Error.InvalidInput.ForField(field: "second", code: "second.invalid", detail: value);
            }
        ));

        var error = result.Should().BeFailureOfType<Error.InvalidInput>().Which;
        error.Fields.Items.Select(field => field.Field.Path).Should().Equal(["/first", "/second"]);
        error.Fields.Items.Select(field => field.Detail).Should().Equal(["invalid", "invalid"]);
        values.Should().Equal(["invalid", "invalid"]);
    }

    [Fact]
    public void EnsureAll_Factories_MixedErrors_ProduceAggregate()
    {
        var result = Result.Ok("value").EnsureAll(
            (_ => false, _ => new Error.Unexpected("first")),
            (_ => false, _ => Error.InvalidInput.ForField(field: "value", code: "value.invalid")));

        result.Should().BeFailureOfType<Error.Aggregate>().Which.Errors.Items.Should().HaveCount(2);
    }

    [Fact]
    public void EnsureAll_Factories_UpstreamFailure_PreservesFailureWithoutCallingDelegates()
    {
        var error = new Error.Unexpected("upstream");
        var original = Result.FailAfterCommit<string>(error);
        var calls = 0;

        var result = original.EnsureAll(
            (_ => { calls++; return false; }, _ => { calls++; return new Error.Unexpected("unused"); }
        ));

        result.Should().BeFailure().Which.Should().Be(error);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeTrue();
        calls.Should().Be(0);
    }

    [Fact]
    public void EnsureAll_Factories_EmptyArray_ReturnsOriginalSuccess()
    {
        var result = Result.Ok("value").EnsureAll(
            Array.Empty<(Func<string, bool>, Func<string, Error>)>());

        result.Should().BeSuccess().Which.Should().Be("value");
    }

    [Fact]
    public void EnsureAll_Factories_NullArray_Throws()
    {
        (Func<string, bool>, Func<string, Error>)[] checks = null!;

        var act = () => Result.Ok("value").EnsureAll(checks);

        act.Should().Throw<ArgumentNullException>().WithParameterName("checks");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnsureAll_Factories_NullDelegate_IdentifiesCheck(bool nullPredicate)
    {
        (Func<string, bool>, Func<string, Error>)[] checks =
        [
            (_ => true, _ => new Error.Unexpected("unused")),
            (nullPredicate ? null! : _ => true, nullPredicate ? _ => new Error.Unexpected("unused") : null!)
        ];

        var act = () => Result.Ok("value").EnsureAll(checks);

        act.Should().Throw<ArgumentNullException>().WithParameterName("checks")
            .And.Message.Should().Contain("checks[1]")
            .And.Contain(nullPredicate ? "predicate" : "errorFactory");
    }

    [Fact]
    public void EnsureAll_Factories_NullProducedError_ThrowsInsteadOfSucceeding()
    {
        var act = () => Result.Ok("value").EnsureAll(
            (_ => false, _ => (Error)null!));

        act.Should().Throw<InvalidOperationException>().WithMessage("*checks[0]*returned null*");
    }

    [Fact]
    public void EnsureAll_ConstantOverload_NullError_RemainsUnambiguous()
    {
        var act = () => Result.Ok("value").EnsureAll((_ => false, null!));

        act.Should().Throw<ArgumentNullException>().WithParameterName("checks")
            .And.Message.Should().Contain(".error is null");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureAllAsync_Factories_FailedChecks_AccumulateLazily(bool valueTask)
    {
        var calls = 0;
        (Func<string, bool>, Func<string, Error>)[] checks =
        [
            (_ => true, _ => { calls++; return new Error.Unexpected("unused"); }),
            (_ => false, value => { calls++; return Error.InvalidInput.ForField(field: "first", code: "first.invalid", detail: value); }),
            (_ => false, value => { calls++; return Error.InvalidInput.ForField(field: "second", code: "second.invalid", detail: value); })
        ];

        var result = valueTask
            ? await new ValueTask<Result<string>>(Result.Ok("value")).EnsureAllAsync(checks)
            : await Task.FromResult(Result.Ok("value")).EnsureAllAsync(checks);

        result.Should().BeFailureOfType<Error.InvalidInput>().Which.Fields.Items.Should().HaveCount(2);
        calls.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureAllAsync_Factories_DeferredReceiver_WaitsBeforeValidation(bool valueTask)
    {
        var completion = new TaskCompletionSource<Result<string>>();
        var calls = 0;
        (Func<string, bool>, Func<string, Error>)[] checks =
        [
            (value => { calls++; return value == "ready"; }, _ => new Error.Unexpected("unused"))
        ];
        var pending = valueTask
            ? new ValueTask<Result<string>>(completion.Task).EnsureAllAsync(checks).AsTask()
            : completion.Task.EnsureAllAsync(checks);

        calls.Should().Be(0);
        completion.SetResult(Result.Ok("ready"));

        (await pending).Should().BeSuccess().Which.Should().Be("ready");
        calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureAllAsync_Factories_UpstreamFailure_SkipsChecks(bool valueTask)
    {
        var error = new Error.Unexpected("upstream");
        var original = Result.FailAfterCommit<string>(error);
        var calls = 0;
        (Func<string, bool>, Func<string, Error>)[] checks =
        [
            (_ => { calls++; return false; }, _ => { calls++; return new Error.Unexpected("unused"); })
        ];

        var result = valueTask
            ? await new ValueTask<Result<string>>(original).EnsureAllAsync(checks)
            : await Task.FromResult(original).EnsureAllAsync(checks);

        result.Should().BeFailure().Which.Should().Be(error);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeTrue();
        calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureAllAsync_ConstantOverload_EmptyChecks_RemainsUnambiguous(bool valueTask)
    {
        var result = valueTask
            ? await new ValueTask<Result<string>>(Result.Ok("value")).EnsureAllAsync()
            : await Task.FromResult(Result.Ok("value")).EnsureAllAsync();

        result.Should().BeSuccess().Which.Should().Be("value");
    }

    [Fact]
    public async Task EnsureAllAsync_Factories_NullTask_Throws()
    {
        Task<Result<string>> task = null!;

        var act = async () => await task.EnsureAllAsync((_ => false, _ => new Error.Unexpected("unused")));

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("resultTask");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureAllAsync_Factories_NullArray_Throws(bool valueTask)
    {
        (Func<string, bool>, Func<string, Error>)[] checks = null!;

        var act = async () =>
        {
            if (valueTask)
                return await new ValueTask<Result<string>>(Result.Ok("value")).EnsureAllAsync(checks);
            return await Task.FromResult(Result.Ok("value")).EnsureAllAsync(checks);
        };

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("checks");
    }
}