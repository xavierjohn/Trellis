using Trellis.Testing;
namespace Trellis.Testing.Tests;

using Trellis;

/// <summary>
/// Tests for MaybeAssertions - testing the testing utilities themselves.
/// </summary>
public class MaybeAssertionsTests
{
    #region HaveValue Tests

    [Fact]
    public void HaveValue_MaybeWithValue_Succeeds()
    {
        // Arrange
        Maybe<string> maybe = "test value";

        // Act & Assert
        maybe.Should().HaveValue();
    }

    [Fact]
    public void HaveValue_MaybeWithValue_ReturnsValue()
    {
        // Arrange
        Maybe<string> maybe = "test value";

        // Act
        var result = maybe.Should().HaveValue();

        // Assert
        result.Which.Should().Be("test value");
    }

    [Fact]
    public void HaveValue_MaybeWithNoValue_Fails()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act & Assert
        var action = () => maybe.Should().HaveValue();
        action.Should().Throw<Exception>()
            .WithMessage("*to have a value*but it was None*");
    }

    [Fact]
    public void HaveValue_WithBecauseClause_IncludesReasonInFailureMessage()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act & Assert
        var action = () => maybe.Should().HaveValue("we expected a user");
        action.Should().Throw<Exception>()
            .WithMessage("*we expected a user*");
    }

    [Fact]
    public void HaveValue_IntegerType_Succeeds()
    {
        // Arrange
        Maybe<int> maybe = 42;

        // Act & Assert
        maybe.Should().HaveValue()
            .Which.Should().Be(42);
    }

    [Fact]
    public void HaveValue_ComplexType_Succeeds()
    {
        // Arrange
        var person = new Person { Name = "John", Age = 30 };
        Maybe<Person> maybe = person;

        // Act & Assert
        maybe.Should().HaveValue()
            .Which.Should().Be(person);
    }

    #endregion

    #region BeNone Tests

    [Fact]
    public void BeNone_MaybeWithNoValue_Succeeds()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act & Assert
        maybe.Should().BeNone();
    }

    [Fact]
    public void BeNone_MaybeWithValue_Fails()
    {
        // Arrange
        Maybe<string> maybe = "test value";

        // Act & Assert
        var action = () => maybe.Should().BeNone();
        action.Should().Throw<Exception>()
            .WithMessage("*to be None*but it had value*test value*");
    }

    [Fact]
    public void BeNone_WithBecauseClause_IncludesReasonInFailureMessage()
    {
        // Arrange
        Maybe<string> maybe = "unexpected value";

        // Act & Assert
        var action = () => maybe.Should().BeNone("the operation should have failed");
        action.Should().Throw<Exception>()
            .WithMessage("*the operation should have failed*");
    }

    [Fact]
    public void BeNone_DefaultMaybe_Succeeds()
    {
        // Arrange
        Maybe<int> maybe = default;

        // Act & Assert
        maybe.Should().BeNone();
    }

    #endregion

    #region HaveValueEqualTo Tests

    [Fact]
    public void HaveValueEqualTo_MaybeWithMatchingValue_Succeeds()
    {
        // Arrange
        Maybe<string> maybe = "test value";

        // Act & Assert
        maybe.Should().HaveValueEqualTo("test value");
    }

    [Fact]
    public void HaveValueEqualTo_MaybeWithDifferentValue_Fails()
    {
        // Arrange
        Maybe<string> maybe = "actual value";

        // Act & Assert
        var action = () => maybe.Should().HaveValueEqualTo("expected value");
        action.Should().Throw<Exception>();
    }

    [Fact]
    public void HaveValueEqualTo_MaybeWithNoValue_Fails()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act & Assert
        var action = () => maybe.Should().HaveValueEqualTo("expected value");
        action.Should().Throw<Exception>()
            .WithMessage("*to have a value*but it was None*");
    }

    [Fact]
    public void HaveValueEqualTo_WithBecauseClause_IncludesReasonInFailureMessage()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act & Assert
        var action = () => maybe.Should().HaveValueEqualTo("expected", "the lookup should succeed");
        action.Should().Throw<Exception>()
            .WithMessage("*the lookup should succeed*");
    }

    [Fact]
    public void HaveValueEqualTo_IntegerValue_Succeeds()
    {
        // Arrange
        Maybe<int> maybe = 42;

        // Act & Assert
        maybe.Should().HaveValueEqualTo(42);
    }

    [Fact]
    public void HaveValueEqualTo_ComplexType_UsesEqualityComparison()
    {
        // Arrange
        var person = new Person { Name = "John", Age = 30 };
        Maybe<Person> maybe = person;

        // Act & Assert
        maybe.Should().HaveValueEqualTo(person);
    }

    [Fact]
    public void HaveValueEqualTo_ComplexType_DifferentInstance_Fails()
    {
        // Arrange
        var person1 = new Person { Name = "John", Age = 30 };
        var person2 = new Person { Name = "Jane", Age = 25 };
        Maybe<Person> maybe = person1;

        // Act & Assert
        var action = () => maybe.Should().HaveValueEqualTo(person2);
        action.Should().Throw<Exception>();
    }

    #endregion

    #region HaveValueMatching Tests

    [Fact]
    public void HaveValueMatching_Should_Pass_When_Predicate_Satisfied()
    {
        // Arrange
        var maybe = Maybe.From(42);

        // Act & Assert
        maybe.Should().HaveValueMatching(x => x > 40);
    }

    [Fact]
    public void HaveValueMatching_Should_Fail_When_Predicate_Not_Satisfied()
    {
        // Arrange
        var maybe = Maybe.From(42);

        // Act
        var act = () => maybe.Should().HaveValueMatching(x => x > 50);

        // Assert
        act.Should().Throw<Exception>()
            .WithMessage("*value to match predicate*");
    }

    [Fact]
    public void HaveValueMatching_Should_Fail_When_None()
    {
        // Arrange
        var maybe = Maybe<int>.None;

        // Act
        var act = () => maybe.Should().HaveValueMatching(x => x > 0);

        // Assert
        act.Should().Throw<Exception>()
            .WithMessage("*to have a value*");
    }

    #endregion

    #region HaveValueEquivalentTo Tests

    [Fact]
    public void HaveValueEquivalentTo_Should_Pass_When_Equivalent()
    {
        // Arrange
        var maybe = Maybe.From(new { Name = "John", Age = 30 });

        // Act & Assert
        maybe.Should().HaveValueEquivalentTo(new { Name = "John", Age = 30 });
    }

    [Fact]
    public void HaveValueEquivalentTo_Should_Fail_When_Not_Equivalent()
    {
        // Arrange
        var maybe = Maybe.From(new { Name = "John", Age = 30 });

        // Act
        var act = () => maybe.Should().HaveValueEquivalentTo(new { Name = "Jane", Age = 25 });

        // Assert
        act.Should().Throw<Exception>();
    }

    #endregion

    #region Chaining Tests

    [Fact]
    public void HaveValue_CanChainAdditionalAssertions()
    {
        // Arrange
        Maybe<string> maybe = "test value";

        // Act & Assert
        maybe.Should().HaveValue()
            .Which.Should().StartWith("test")
            .And.EndWith("value")
            .And.HaveLength(10);
    }

    [Fact]
    public void HaveValueEqualTo_ReturnsAndConstraint()
    {
        // Arrange
        Maybe<int> maybe = 42;

        // Act & Assert
        var result = maybe.Should().HaveValueEqualTo(42);
        result.Should().NotBeNull();
    }

    [Fact]
    public void BeNone_ReturnsAndConstraint()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act & Assert
        var result = maybe.Should().BeNone();
        result.Should().NotBeNull();
    }

    #endregion

    #region Different Type Tests

    [Fact]
    public void MaybeAssertions_WithDateTime_WorksCorrectly()
    {
        // Arrange
        var now = DateTime.Now;
        Maybe<DateTime> maybe = now;

        // Act & Assert
        maybe.Should().HaveValue()
            .Which.Should().Be(now);
    }

    [Fact]
    public void MaybeAssertions_WithGuid_WorksCorrectly()
    {
        // Arrange
        var guid = Guid.NewGuid();
        Maybe<Guid> maybe = guid;

        // Act & Assert
        maybe.Should().HaveValueEqualTo(guid);
    }

    [Fact]
    public void MaybeAssertions_WithEnum_WorksCorrectly()
    {
        // Arrange
        Maybe<Status> maybe = Status.Active;

        // Act & Assert
        maybe.Should().HaveValue()
            .Which.Should().Be(Status.Active);
    }

    [Fact]
    public void MaybeAssertions_WithList_WorksCorrectly()
    {
        // Arrange
        var list = new List<int> { 1, 2, 3 };
        Maybe<List<int>> maybe = list;

        // Act & Assert
        maybe.Should().HaveValue()
            .Which.Should().BeEquivalentTo(list);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void HaveValue_EmptyString_Succeeds()
    {
        // Arrange
        Maybe<string> maybe = string.Empty;

        // Act & Assert
        maybe.Should().HaveValue()
            .Which.Should().BeEmpty();
    }

    [Fact]
    public void HaveValue_ZeroInteger_Succeeds()
    {
        // Arrange
        Maybe<int> maybe = 0;

        // Act & Assert
        maybe.Should().HaveValue()
            .Which.Should().Be(0);
    }

    [Fact]
    public void BeNone_AfterCreatingFromNullable_Succeeds()
    {
        // Arrange
        string? nullValue = null;
        Maybe<string> maybe = nullValue == null ? Maybe<string>.None : nullValue;

        // Act & Assert
        maybe.Should().BeNone();
    }

    #endregion

    #region Integration with Result Tests

    [Fact]
    public void MaybeAssertions_WithResultToMaybe_WorksCorrectly()
    {
        // Arrange
        var result = Result.Ok("test");
        var maybe = result.IsSuccess ? Maybe.From(result.Unwrap()) : Maybe<string>.None;

        // Act & Assert
        maybe.Should().HaveValueEqualTo("test");
    }

    [Fact]
    public void MaybeAssertions_FromFailedResult_IsNone()
    {
        // Arrange
        var result = Result.Fail<string>(Error.NotFound("Not found"));
        var maybe = result.IsSuccess ? Maybe.From(result.Unwrap()) : Maybe<string>.None;

        // Act & Assert
        maybe.Should().BeNone();
    }

    #endregion

    // Helper classes for testing
    private class Person
    {
        public string? Name { get; set; }
        public int Age { get; set; }

        public override bool Equals(object? obj)
        {
            if (obj is Person other)
                return Name == other.Name && Age == other.Age;
            return false;
        }

        public override int GetHashCode() => HashCode.Combine(Name, Age);
    }

    private enum Status
    {
        Active,
        Inactive,
        Pending
    }
}
