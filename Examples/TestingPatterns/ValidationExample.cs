using Trellis.Testing;
using Trellis.Primitives;

namespace Example;

using System.Collections.Immutable;
using Trellis;
using static Result;
using static Trellis.Error.UnprocessableContent;

public class ValidationExample
{
    [Fact]
    public void Validation_successful_Test()
    {
        var createdAt = DateTime.UtcNow;
        var updatedAt = createdAt;

        var actual = EmailAddress.TryCreate("xavier@somewhere.com")
            .Combine(FirstName.TryCreate("Xavier"))
            .Combine(LastName.TryCreate("John"))
            .Combine(Ensure(createdAt <= updatedAt, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "updateAt cannot be less than createdAt" }))
            .Bind((email, firstName, lastName) => Result.Ok(string.Join(" ", firstName, lastName, email)));

        actual.Unwrap().Should().Be("Xavier John xavier@somewhere.com");
    }

    [Fact]
    public void Validation_failed_Test()
    {
        // Arrange
        ImmutableArray<FieldViolation> expected = [
            new(InputPointer.ForProperty("lastName"), "validation.error") { Detail = "Last Name cannot be empty." },
            new(InputPointer.ForProperty("email"), "validation.error") { Detail = "Email address is not valid." },
            new(InputPointer.ForProperty("updatedAt"), "validation.error") { Detail = "updateAt cannot be less than createdAt" },
        ];

        var createdAt = DateTime.UtcNow;
        var updatedAt = createdAt.AddMinutes(-10);

        //Act
        var actual = FirstName.TryCreate("Xavier")
            .Combine(LastName.TryCreate(string.Empty))
            .Combine(EmailAddress.TryCreate("xavier @ somewhereelse.com"))
            .Combine(Ensure(createdAt <= updatedAt, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(updatedAt)), "validation.error") { Detail = "updateAt cannot be less than createdAt" }))))
            .Bind((firstName, lastName, email) =>
            {
                true.Should().BeFalse("this code should not get executed");
                return Result.Ok(string.Join(" ", firstName, lastName, email));
            });
        // Assert
        actual.IsFailure.Should().BeTrue();
        var validationErrors = (Error.UnprocessableContent)actual.UnwrapError();
        validationErrors.Fields.Items.Should().HaveCount(3);
        validationErrors.Fields.Items.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Convert_optional_primitive_type_to_valid_objects()
    {
        string? firstName = null; // Optional so null is allowed.
        string email = "xavier@somewhere.com";
        string? lastName = "John";

        var actual = EmailAddress.TryCreate(email)
            .Combine(Maybe.Optional(firstName, s => FirstName.TryCreate(s)))
            .Combine(Maybe.Optional(lastName, s => LastName.TryCreate(s)))
            .Bind(Add);

        actual.Unwrap().Should().Be("xavier@somewhere.com  John");

        static Result<string> Add(EmailAddress emailAddress, Maybe<FirstName> firstname, Maybe<LastName> lastname)
            => Result.Ok(emailAddress + " " + firstname + " " + lastname);
    }

    [Fact]
    public void Cannot_convert_optional_invalid_primitive_type_to_valid_objects()
    {
        string? firstName = string.Empty; // Optional but empty string is not a valid first name.
        string email = "xavier@somewhere.com";
        string? lastName = "John";

        var actual = EmailAddress.TryCreate(email)
            .Combine(Maybe.Optional(firstName, s => FirstName.TryCreate(s)))
            .Combine(Maybe.Optional(lastName, s => LastName.TryCreate(s)))
            .Bind(Add);

        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().BeOfType<Error.UnprocessableContent>();
        var validationError = (Error.UnprocessableContent)actual.UnwrapError();
        validationError.Fields[0].Detail.Should().Be("First Name cannot be empty.");

        static Result<string> Add(EmailAddress emailAddress, Maybe<FirstName> firstname, Maybe<LastName> lastname)
            => Result.Ok(emailAddress + " " + firstname + " " + lastname);
    }
}
