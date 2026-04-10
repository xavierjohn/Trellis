namespace Trellis.EntityFrameworkCore.Tests.Helpers;

using Trellis.Primitives;

/// <summary>
/// Test composite value object that contains a <c>partial Maybe&lt;T&gt;</c> scalar property.
/// Used to validate that <see cref="MaybeConvention"/> correctly maps <c>Maybe&lt;T&gt;</c>
/// scalar properties inside owned <see cref="ValueObject"/> types — not just on entity types.
/// </summary>
[OwnedEntity]
public partial class TestContactInfo : ValueObject
{
    public string Name { get; private set; }

    /// <summary>
    /// Optional scalar VO inside a composite VO — the scenario under test.
    /// The source generator emits a <c>private PhoneNumber? _phone</c> backing field.
    /// <see cref="MaybeConvention"/> must discover and map this backing field even though
    /// the owning type is an owned <see cref="ValueObject"/>, not a root entity.
    /// </summary>
    public partial Maybe<PhoneNumber> Phone { get; private set; }

    public TestContactInfo(string name, Maybe<PhoneNumber> phone)
    {
        Name = name;
        Phone = phone;
    }

    public static TestContactInfo Create(string name, Maybe<PhoneNumber> phone) =>
        new(name, phone);

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Name;
        yield return Phone.HasValue ? (string)Phone.Value : null;
    }
}