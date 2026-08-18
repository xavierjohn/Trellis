namespace Trellis.Core.Tests;

using Trellis.Testing;

/// <summary>
/// Tests for <see cref="Result.FailAfterCommit{TValue}(Error)"/> and
/// <see cref="Result.FailAfterCommit(Error)"/> (Issue #533): a persist-on-failure outcome
/// that signals to <c>TransactionalCommandBehavior</c> that staged changes should still be
/// committed even though the handler returned failure.
/// </summary>
/// <remarks>
/// <para>
/// <c>FailAfterCommit</c> is semantically <em>still a failure</em> — <see cref="IResult.IsFailure"/>
/// is <see langword="true"/> and the error propagates to the caller. The only behavioral
/// difference is the per-instance <see cref="IPersistOnFailure.PersistOnFailure"/> flag that
/// opt-in pipeline behaviors (notably <c>TransactionalCommandBehavior</c>) consult to decide
/// whether to commit staged work alongside the failed outcome.
/// </para>
/// <para>
/// The flag is a <em>per-instance</em> property rather than a pure marker interface so that
/// ordinary <see cref="Result.Fail{TValue}(Error)"/> values (which also implement
/// <see cref="IPersistOnFailure"/> via the same struct) cannot be mistaken for persist-on-failure
/// outcomes by a type-only <c>is IPersistOnFailure</c> check.
/// </para>
/// </remarks>
public class ResultFailAfterCommitTests
{
    [Fact]
    public void FailAfterCommit_generic_returns_failure_with_persist_flag_set()
    {
        var error = new Error.Conflict(null, "domain.violation") { Detail = "bad" };

        var result = Result.FailAfterCommit<string>(error);

        result.Should().BeFailure();
        result.Error.Should().BeSameAs(error);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeTrue();
    }

    [Fact]
    public void FailAfterCommit_unit_returns_failure_with_persist_flag_set()
    {
        var error = new Error.Conflict(null, "domain.violation") { Detail = "bad" };

        var result = Result.FailAfterCommit(error);

        result.Should().BeFailure();
        result.Error.Should().BeSameAs(error);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeTrue();
    }

    [Fact]
    public void Ordinary_Fail_has_persist_flag_false()
    {
        var error = new Error.Conflict(null, "domain.violation") { Detail = "bad" };

        var result = Result.Fail<string>(error);

        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse(
            "Result.Fail must not opt into persist-on-failure; only Result.FailAfterCommit sets the flag");
    }

    [Fact]
    public void Ordinary_unit_Fail_has_persist_flag_false()
    {
        var error = new Error.Conflict(null, "domain.violation") { Detail = "bad" };

        var result = Result.Fail(error);

        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse();
    }

    [Fact]
    public void Ok_has_persist_flag_false()
    {
        var result = Result.Ok("hello");

        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse(
            "the flag is meaningless on success and must not be true; the behavior check uses 'IsSuccess || PersistOnFailure'");
    }

    [Fact]
    public void Default_initialised_Result_has_persist_flag_false()
    {
        var result = default(Result<string>);

        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse(
            "default(Result<T>) is a sentinel failure with no opt-in to persist-on-failure semantics");
    }

    [Fact]
    public void FailAfterCommit_and_Fail_with_same_error_are_not_equal()
    {
        var error = new Error.Conflict(null, "domain.violation") { Detail = "bad" };

        var failAfterCommit = Result.FailAfterCommit<string>(error);
        var fail = Result.Fail<string>(error);

        failAfterCommit.Equals(fail).Should().BeFalse(
            "two failures that produce different commit behavior must not be equal — equality must reflect persist-on-failure intent");
    }

    [Fact]
    public void Two_FailAfterCommit_with_same_error_are_equal()
    {
        var error = new Error.Conflict(null, "domain.violation") { Detail = "bad" };

        var a = Result.FailAfterCommit<string>(error);
        var b = Result.FailAfterCommit<string>(error);

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void AsUnit_propagates_persist_flag()
    {
        var error = new Error.Conflict(null, "domain.violation") { Detail = "bad" };

        var unitResult = Result.FailAfterCommit<string>(error).AsUnit();

        unitResult.Should().BeFailure();
        ((IPersistOnFailure)unitResult).PersistOnFailure.Should().BeTrue(
            "AsUnit() must preserve persist-on-failure so the discarded-value bridge keeps its commit intent");
    }

    [Fact]
    public void CreateFailure_static_factory_does_not_set_persist_flag()
    {
        // IFailureFactory<TSelf>.CreateFailure is used by generic pipeline behaviors (e.g.
        // TransactionalCommandBehavior wraps the commit error via TResponse.CreateFailure(error)).
        // It must NOT promote to persist-on-failure — that would create an infinite-loop hazard
        // where a commit failure stays "persist on failure" and the next attempted commit also fails.
        var error = new Error.Conflict(null, "commit_failed") { Detail = "row was concurrently modified" };

        var result = Result<string>.CreateFailure(error);

        result.Should().BeFailure();
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse();
    }

    [Fact]
    public void FailAfterCommit_generic_with_null_error_throws() =>
        FluentActions
            .Invoking(() => Result.FailAfterCommit<string>(null!))
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public void FailAfterCommit_unit_with_null_error_throws() =>
        FluentActions
            .Invoking(() => Result.FailAfterCommit(null!))
            .Should().Throw<ArgumentNullException>();
}