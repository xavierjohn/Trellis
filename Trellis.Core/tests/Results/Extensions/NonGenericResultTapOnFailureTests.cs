namespace Trellis.Core.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for the non-generic Result.TapOnFailure / TapOnFailureAsync overloads (ga-04 rename).
/// The non-generic surface mirrors the generic Result&lt;T&gt; rename: TapError → TapOnFailure
/// (and TapErrorAsync → TapOnFailureAsync) so consumers see one consistent failure-side verb name.
/// </summary>
public class NonGenericResultTapOnFailureTests : TestBase
{
    #region Sync — Result.TapOnFailure

    [Fact]
    public void TapOnFailure_OnFailure_InvokesAction_AndReturnsOriginal()
    {
        Error? captured = null;
        var input = Result.Fail(Error1);

        var output = input.TapOnFailure(err => captured = err);

        captured.Should().Be(Error1);
        output.Should().BeFailure().Which.Should().HaveCode(Error1.Code);
    }

    [Fact]
    public void TapOnFailure_OnSuccess_DoesNotInvokeAction()
    {
        var invoked = false;

        var output = Result.Ok().TapOnFailure(_ => invoked = true);

        invoked.Should().BeFalse();
        output.Should().BeSuccess();
    }

    [Fact]
    public void TapOnFailure_NullAction_Throws()
    {
        var input = Result.Fail(Error1);
        Action<Error> action = null!;

        Action act = () => input.TapOnFailure(action);

        act.Should().Throw<ArgumentNullException>().Where(e => e.ParamName == "action");
    }

    #endregion

    #region Async — TapOnFailureAsync

    [Fact]
    public async Task TapOnFailureAsync_TaskWithAction_OnFailure_InvokesAction()
    {
        Error? captured = null;
        Task<Result> input = Task.FromResult(Result.Fail(Error1));

        var output = await input.TapOnFailureAsync(err => captured = err);

        captured.Should().Be(Error1);
        output.Should().BeFailure();
    }

    [Fact]
    public async Task TapOnFailureAsync_ResultWithFuncTask_OnFailure_AwaitsFunc()
    {
        var invoked = false;
        var input = Result.Fail(Error1);

        var output = await input.TapOnFailureAsync(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        invoked.Should().BeTrue();
        output.Should().BeFailure();
    }

    [Fact]
    public async Task TapOnFailureAsync_TaskWithFuncTask_OnSuccess_DoesNotInvoke()
    {
        var invoked = false;
        Task<Result> input = Task.FromResult(Result.Ok());

        var output = await input.TapOnFailureAsync(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        invoked.Should().BeFalse();
        output.Should().BeSuccess();
    }

    [Fact]
    public async Task TapOnFailureAsync_ValueTaskWithAction_OnFailure_InvokesAction()
    {
        Error? captured = null;
        ValueTask<Result> input = new(Result.Fail(Error1));

        var output = await input.TapOnFailureAsync(err => captured = err);

        captured.Should().Be(Error1);
        output.Should().BeFailure();
    }

    [Fact]
    public async Task TapOnFailureAsync_ResultWithFuncValueTask_OnFailure_AwaitsFunc()
    {
        var invoked = false;
        var input = Result.Fail(Error1);

        var output = await input.TapOnFailureAsync(_ =>
        {
            invoked = true;
            return ValueTask.CompletedTask;
        });

        invoked.Should().BeTrue();
        output.Should().BeFailure();
    }

    [Fact]
    public async Task TapOnFailureAsync_ValueTaskWithFuncValueTask_RoundTrips()
    {
        var invoked = 0;
        ValueTask<Result> input = new(Result.Fail(Error1));

        var output = await input.TapOnFailureAsync(_ =>
        {
            invoked++;
            return ValueTask.CompletedTask;
        });

        invoked.Should().Be(1);
        output.Should().BeFailure();
    }

    #endregion

    #region API surface — TapError no longer present (ga-04)

    [Fact]
    public void NonGenericResult_does_not_expose_TapError_extension_method()
    {
        // ga-04: the legacy non-generic Result.TapError extension was renamed to TapOnFailure
        // to converge with the generic Result<T> surface. Only TapOnFailure should exist.
        var nonGenericResultExtensionsAssembly = typeof(Result).Assembly;
        var tapErrorMethods = nonGenericResultExtensionsAssembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            .Where(m => (m.Name == "TapError" || m.Name == "TapErrorAsync")
                        && m.GetParameters().Length > 0
                        && (m.GetParameters()[0].ParameterType == typeof(Result)
                            || m.GetParameters()[0].ParameterType == typeof(Task<Result>)
                            || m.GetParameters()[0].ParameterType == typeof(ValueTask<Result>)))
            .ToList();

        tapErrorMethods.Should().BeEmpty(
            "non-generic Result.TapError/TapErrorAsync was renamed to TapOnFailure/TapOnFailureAsync in v2 (ga-04). " +
            "Found: " + string.Join(", ", tapErrorMethods.Select(m => $"{m.DeclaringType!.Name}.{m.Name}")));
    }

    #endregion
}
