namespace Trellis.EntityFrameworkCore;

/// <summary>
/// Marks a composite <see cref="Trellis.ValueObject"/> as an EF Core owned entity type,
/// triggering source generation of the private parameterless constructor required for EF Core
/// materialization.
/// </summary>
/// <remarks>
/// <para>
/// The decorated type must be <c>partial</c> and inherit from <see cref="Trellis.ValueObject"/>.
/// The source generator emits a private parameterless constructor that initializes all
/// reference-type properties with <c>null!</c> to satisfy the compiler's nullability analysis.
/// </para>
/// <para>Properties should be settable (for example, with <c>private set;</c>) so EF Core can populate them during materialization.</para>
/// </remarks>
/// <example>
/// <code><![CDATA[
/// [OwnedEntity]
/// public partial class Address : ValueObject
/// {
///     public string Street { get; private set; }
///     public string City { get; private set; }
///     public string State { get; private set; }
///
///     public Address(string street, string city, string state)
///     {
///         Street = street;
///         City = city;
///         State = state;
///     }
///
///     protected override IEnumerable<IComparable?> GetEqualityComponents()
///     {
///         yield return Street;
///         yield return City;
///         yield return State;
///     }
/// }
/// // Generator emits:
/// // partial class Address
/// // {
/// //     private Address()
/// //     {
/// //         Street = null!;
/// //         City = null!;
/// //         State = null!;
/// //     }
/// // }
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class OwnedEntityAttribute : Attribute;