namespace Trellis.Core.Tests.Maybes;

using Trellis;

public class MaybeTests
{
    [Fact]
    public void Can_create_a_nullable_maybe()
    {
        Maybe<MyClass> maybe = default;

        maybe.HasValue.Should().BeFalse();
        maybe.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Can_create_a_maybe_none()
    {
        var maybe = Maybe<MyClass>.None;

        maybe.HasValue.Should().BeFalse();
        maybe.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Nullable_maybe_is_same_as_maybe_none()
    {
        Maybe<MyClass> nullableMaybe = default;
        var maybeNone = Maybe<MyClass>.None;

        nullableMaybe.Should().Be(maybeNone);
    }

    [Fact]
    public void Cannot_access_Value_if_none()
    {
        Maybe<MyClass> maybe = default;

        Action action = () =>
        {
            var myClass = maybe.GetValueOrThrow();
        };

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Can_create_a_non_nullable_maybe()
    {
        var instance = new MyClass();

        Maybe<MyClass> maybe = instance;

        maybe.HasValue.Should().BeTrue();
        maybe.HasNoValue.Should().BeFalse();
        maybe.GetValueOrThrow().Should().Be(instance);
    }

    [Fact]
    public void ToString_returns_Empty_String_for_no_value()
    {
        Maybe<MyClass> maybe = default;

        var str = maybe.ToString();

        str.Should().Be(string.Empty);
    }

    [Fact]
    public void ToString_returns_underlying_objects_string_representation()
    {
        Maybe<MyClass> maybe = new MyClass();

        var str = maybe.ToString();

        str.Should().Be("My custom class");
    }

    [Fact]
    public void Maybe_None_has_no_value()
    {
        Maybe<string>.None.HasValue.Should().BeFalse();
        Maybe<int>.None.HasValue.Should().BeFalse();
    }

    [Fact]
    public void Maybe_None_Tuples_has_no_value_is_true()
    {
        Maybe<(Array, Exception)>.None.HasNoValue.Should().BeTrue();
        Maybe<(double, int, byte)>.None.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Maybe_None_Tuples_has_value_is_false()
    {
        Maybe<(DateTime, bool, char)>.None.HasValue.Should().BeFalse();
        Maybe<(string, TimeSpan)>.None.HasValue.Should().BeFalse();
    }

    [Fact]
    public void Maybe_From_without_type_parameter_creates_new_maybe()
    {
        var withoutTypeParam = Maybe.From("test");
        var withTypeParam = Maybe.From<string>("test");
        var differentValueTypeParam = Maybe.From<string>("tests");

        withoutTypeParam.Should().Be(withTypeParam);
        withoutTypeParam.Should().NotBe(differentValueTypeParam);
    }

    [Fact]
    public void Can_cast_non_generic_maybe_none_to_maybe_none()
    {
        Maybe<int> maybe = Maybe<int>.None;

        maybe.HasValue.Should().BeFalse();
        maybe.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void GetValueOrThrww_throws_with_message_if_source_is_empty()
    {
        const string errorMessage = "Maybe is none";

        Action action = () =>
        {
            var maybe = Maybe<int>.None;
            maybe.GetValueOrThrow(errorMessage);
        };

        action.Should().Throw<InvalidOperationException>().WithMessage(errorMessage);
    }

    [Fact]
    public void GetValueOrThrow_returns_value_if_source_has_value()
    {
        const int value = 5;
        var maybe = Maybe.From(value);

        const string errorMessage = "Maybe is none";
        var result = maybe.GetValueOrThrow(errorMessage);

        result.Should().Be(value);
    }

    [Fact]
    public void TryGetValue_returns_false_if_source_is_empty()
    {
        var maybe = Maybe<int>.None;

        var result = maybe.TryGetValue(out var value);

        result.Should().BeFalse();
        value.Should().Be(default);
    }

    [Fact]
    public void TryGetValue_returns_true_if_source_has_value()
    {
        var maybe = Maybe.From(5);

        var result = maybe.TryGetValue(out var value);

        result.Should().BeTrue();
        value.Should().Be(5);
    }

    [Fact]
    public void Maybe_struct_default_is_none()
    {
        Maybe<int> maybe = default;

        maybe.HasValue.Should().BeFalse();
        maybe.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Maybe_struct_value_is_some()
    {
        Maybe<int> maybe = 5;

        maybe.HasValue.Should().BeTrue();
        maybe.HasNoValue.Should().BeFalse();
    }

    [Fact]
    public void Maybe_class_null_is_none()
    {
        Maybe<MyClass> maybe = default;

        maybe.HasValue.Should().BeFalse();
        maybe.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Maybe_implicit_operator_converts_false_to_some_false()
    {
        Maybe<bool> m = false;

        m.HasValue.Should().BeTrue();
        m.GetValueOrThrow().Should().BeFalse();
    }

    [Fact]
    public void From_on_generic_struct_creates_maybe_with_value()
    {
        var instance = new MyClass();

        var maybe = Maybe<MyClass>.From(instance);

        maybe.HasValue.Should().BeTrue();
        maybe.GetValueOrThrow().Should().Be(instance);
    }

    [Fact]
    public void From_on_generic_struct_with_null_creates_none()
    {
        var maybe = Maybe<MyClass>.From(null!);

        maybe.HasValue.Should().BeFalse();
    }

    [Fact]
    public void From_on_generic_struct_matches_static_helper()
    {
        var instance = new MyClass();

        var fromStruct = Maybe<MyClass>.From(instance);
        var fromHelper = Maybe.From(instance);

        fromStruct.Should().Be(fromHelper);
    }

    private class MyClass
    {
        public override string ToString() => "My custom class";
    }
}