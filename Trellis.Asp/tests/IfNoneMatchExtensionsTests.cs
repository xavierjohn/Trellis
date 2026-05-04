namespace Trellis.Asp.Tests;

using Trellis.Testing;

/// <summary>
/// Tests for <see cref="IfNoneMatchExtensions"/> — create-if-absent pattern helpers.
/// </summary>
public class IfNoneMatchExtensionsTests
{
    [Fact]
    public void EnforceIfNoneMatchPrecondition_NullETags_ReturnsOriginalResult()
    {
        var result = Result.Ok("value");

        var actual = result.EnforceIfNoneMatchPrecondition(null);

        actual.Should().BeSuccess();
        actual.Should().HaveValue("value");
    }

    [Fact]
    public void EnforceIfNoneMatchPrecondition_WildcardOnSuccess_ReturnsPreconditionFailed()
    {
        var result = Result.Ok("value");

        var actual = result.EnforceIfNoneMatchPrecondition([EntityTagValue.Wildcard()]);

        actual.Should().BeFailureOfType<Error.PreconditionFailed>();
    }

    [Fact]
    public void EnforceIfNoneMatchPrecondition_WildcardOnFailure_PreservesOriginalError()
    {
        var result = Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "not found" });

        var actual = result.EnforceIfNoneMatchPrecondition([EntityTagValue.Wildcard()]);

        actual.Should().BeFailureOfType<Error.NotFound>();
    }

    [Fact]
    public void EnforceIfNoneMatchPrecondition_NonWildcardTags_ReturnsOriginalResult()
    {
        var result = Result.Ok("value");

        var actual = result.EnforceIfNoneMatchPrecondition([EntityTagValue.Strong("abc123"), EntityTagValue.Strong("def456")]);

        actual.Should().BeSuccess();
        actual.Should().HaveValue("value");
    }

    [Fact]
    public void EnforceIfNoneMatchPrecondition_EmptyArray_ReturnsOriginalResult()
    {
        var result = Result.Ok("value");

        var actual = result.EnforceIfNoneMatchPrecondition([]);

        actual.Should().BeSuccess();
        actual.Should().HaveValue("value");
    }

    [Fact]
    public async Task EnforceIfNoneMatchPreconditionAsync_Task_WildcardOnSuccess_ReturnsPreconditionFailed()
    {
        var resultTask = Task.FromResult(Result.Ok("value"));

        var actual = await resultTask.EnforceIfNoneMatchPreconditionAsync([EntityTagValue.Wildcard()]);

        actual.Should().BeFailureOfType<Error.PreconditionFailed>();
    }

    [Fact]
    public async Task EnforceIfNoneMatchPreconditionAsync_ValueTask_WildcardOnSuccess_ReturnsPreconditionFailed()
    {
        var resultTask = new ValueTask<Result<string>>(Result.Ok("value"));

        var actual = await resultTask.EnforceIfNoneMatchPreconditionAsync([EntityTagValue.Wildcard()]);

        actual.Should().BeFailureOfType<Error.PreconditionFailed>();
    }

    /// <summary>
    /// Regression for inspection finding m-7: <c>EnforceIfNoneMatchPrecondition&lt;T&gt;</c> previously
    /// constructed <c>new ResourceRef(typeof(T).Name, null)</c>, which leaks CLR backtick mangling for
    /// closed-generic Ts and ignores documented wrapper-peeling for <see cref="Maybe{T}"/>. The fix
    /// routes through <see cref="ResourceRef.For{TResource}(object?)"/> so both concerns are handled
    /// in one place.
    /// </summary>
    [Fact]
    public void EnforceIfNoneMatchPrecondition_ResourceRef_uses_friendly_type_name_for_closed_generic_T()
    {
        var result = Result.Ok(new List<string>());

        var actual = result.EnforceIfNoneMatchPrecondition([EntityTagValue.Wildcard()]);

        actual.Should().BeFailureOfType<Error.PreconditionFailed>();
        actual.TryGetError(out var error).Should().BeTrue();
        var pf = (Error.PreconditionFailed)error!;
        pf.Resource.Type.Should().Be("List",
            "m-7: backtick-mangled CLR names like 'List`1' must not leak through to the resource ref");
    }

    [Fact]
    public void EnforceIfNoneMatchPrecondition_ResourceRef_peels_Maybe_wrapper_to_expose_inner_domain()
    {
        var result = Result.Ok(Maybe.From("value"));

        var actual = result.EnforceIfNoneMatchPrecondition([EntityTagValue.Wildcard()]);

        actual.Should().BeFailureOfType<Error.PreconditionFailed>();
        actual.TryGetError(out var error).Should().BeTrue();
        var pf = (Error.PreconditionFailed)error!;
        pf.Resource.Type.Should().Be("String",
            "m-7: Maybe<T> wrappers must be peeled so the meaningful inner domain name is the resource type");
    }
}