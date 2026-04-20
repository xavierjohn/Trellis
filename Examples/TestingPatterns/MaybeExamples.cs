namespace TestingPatterns;

using Trellis;
using Xunit;
public class MaybeExamples
{
    [Fact]
    public void Explicit_construction()
    {
        // Arrange
        Maybe<string> apple1 = Maybe.From("apple");
        var apple2 = Maybe.From("apple");

        // Assert
        apple1.Should().BeOfType<Maybe<string>>();
        apple2.Should().BeOfType<Maybe<string>>();
        apple1.Value.Should().Be("apple");
        apple2.Value.Should().Be("apple");
    }

    [Fact]
    public void Construction_None_X2F_No_Value()
    {
        // Arrange
        Maybe<string> fruit1 = Maybe<string>.None;
        Maybe<string> fruit2 = default; // reference type
        Maybe<int> fruit3 = default; // value type

        // Assert
        fruit1.Should().BeOfType<Maybe<string>>();
        fruit1.HasNoValue.Should().BeTrue();

        fruit2.Should().BeOfType<Maybe<string>>();
        fruit2.HasNoValue.Should().BeTrue();

        fruit3.Should().BeOfType<Maybe<int>>();
        fruit3.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Implicit_Conversion()
    {
        // Arrange
        Maybe<string> apple = "apple"; // implicit conversion

        // Or as a method return value
        static Maybe<string> GetFruit(string fruit)
        {
            if (string.IsNullOrWhiteSpace(fruit))
                return Maybe<string>.None;

            return fruit; // implicit conversion
        }

        // Act
        var fruit = GetFruit("apple");

        // Assert
        apple.Should().BeOfType<Maybe<string>>();
        apple.Value.Should().Be("apple");

        fruit.Should().BeOfType<Maybe<string>>();
        fruit.Value.Should().Be("apple");

    }

    [Fact]
    public void Equality()
    {
        // Arrange
        Maybe<string> apple = "apple";
        Maybe<string> orange = "orange";
        string alsoOrange = "orange";
        Maybe<string> noFruit = Maybe<string>.None;

        // Act

        // Assert
        apple.Should().NotBe(orange);
        orange.Should().Be(alsoOrange);
        noFruit.Should().NotBe(orange);
    }

    [Fact]
    public void Convert_to_string()
    {
        // Arrange
        Maybe<string> apple = "apple";
        Maybe<string> noFruit = Maybe<string>.None;

        // Act

        // Assert
        apple.ToString().Should().Be("apple");
        noFruit.ToString().Should().Be(string.Empty);
    }

    [Fact]
    public void GetValueOrThrow()
    {
        // Arrange
        Maybe<string> apple = "apple";
        Maybe<string> noFruit = Maybe<string>.None;

        // Act
        var action1 = () => apple.GetValueOrThrow();
        var action2 = () => noFruit.GetValueOrThrow();

        // Assert
        action1.Should().NotThrow<InvalidOperationException>();
        action2.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void HasValue_and_HasNoValue()
    {
        // Arrange
        Maybe<string> apple = "apple";
        Maybe<string> noFruit = Maybe<string>.None;

        // Act

        // Assert
        apple.HasValue.Should().BeTrue();
        noFruit.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void GetValueOrDefault()
    {
        // Arrange
        Maybe<string> apple = "apple";
        Maybe<string> unknownFruit = Maybe<string>.None;

        // Act
        string appleValue = apple.GetValueOrDefault("banana");
        string unknownFruitValue = unknownFruit.GetValueOrDefault("banana");

        // Assert
        apple.Should().Be("apple");
        unknownFruitValue.Should().Be("banana");
    }
}
