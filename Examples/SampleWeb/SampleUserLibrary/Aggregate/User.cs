namespace SampleUserLibrary;

using Trellis;
using Trellis.Primitives;

/// <summary>
/// User aggregate. Demonstrates pure ROP construction (no FluentValidation):
/// scalar VOs are validated at the wire boundary; composite/business invariants
/// are enforced by chaining <c>Ensure</c> guards in <see cref="TryCreate"/>.
/// </summary>
public class User : Aggregate<UserId>
{
    // Password complexity rules (kept here, not in DTO, because they're a domain
    // invariant — even an admin-created user must satisfy them).
    private const int MinPasswordLength = 8;
    private const string PasswordRulesField = "Password";

    public FirstName FirstName { get; }
    public LastName LastName { get; }
    public EmailAddress Email { get; }
    public PhoneNumber Phone { get; }
    public Age Age { get; }
    public CountryCode Country { get; }
    public Maybe<Url> Website { get; }
    public string Password { get; }

    public static Result<User> TryCreate(
        FirstName firstName,
        LastName lastName,
        EmailAddress email,
        PhoneNumber phone,
        Age age,
        CountryCode country,
        string password,
        Maybe<Url> website = default)
    {
        ArgumentNullException.ThrowIfNull(firstName);
        ArgumentNullException.ThrowIfNull(lastName);
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(phone);
        ArgumentNullException.ThrowIfNull(age);
        ArgumentNullException.ThrowIfNull(country);

        return Result.Ok(password)
            .Ensure(p => age.Value >= 18,
                Field("Age", "User must be at least 18 years old to register."))
            .Ensure(p => !string.IsNullOrEmpty(p),
                Field(PasswordRulesField, "Password must not be empty."))
            .Ensure(p => p.Length >= MinPasswordLength,
                Field(PasswordRulesField, $"Password must be at least {MinPasswordLength} characters long."))
            .Ensure(p => p.Any(char.IsUpper),
                Field(PasswordRulesField, "Password must contain at least one uppercase letter."))
            .Ensure(p => p.Any(char.IsLower),
                Field(PasswordRulesField, "Password must contain at least one lowercase letter."))
            .Ensure(p => p.Any(char.IsDigit),
                Field(PasswordRulesField, "Password must contain at least one number."))
            .Ensure(p => p.Any(c => !char.IsLetterOrDigit(c)),
                Field(PasswordRulesField, "Password must contain at least one special character."))
            .Map(_ => new User(firstName, lastName, email, phone, age, country, password, website));
    }

    private static Error.UnprocessableContent Field(string property, string detail) =>
        new(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty(property), "validation.error") { Detail = detail }));

    // EF Core parameterless constructor
    private User() : base(default!)
    {
        FirstName = null!;
        LastName = null!;
        Email = null!;
        Phone = null!;
        Age = null!;
        Country = null!;
        Password = null!;
    }

    private User(
        FirstName firstName,
        LastName lastName,
        EmailAddress email,
        PhoneNumber phone,
        Age age,
        CountryCode country,
        string password,
        Maybe<Url> website)
        : base(UserId.NewUniqueV4())
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Phone = phone;
        Age = age;
        Country = country;
        Website = website;
        Password = password;
    }
}
