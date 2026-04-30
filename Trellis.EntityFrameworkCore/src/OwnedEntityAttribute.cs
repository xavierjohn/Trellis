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
/// <para>Properties should be settable so EF Core can populate them during materialization. Use <c>{ get; private set; }</c> as the supported, tested pattern.</para>
/// <para>
/// <strong>Note — init-only properties.</strong> <c>{ get; init; }</c> on properties of
/// <c>[OwnedEntity]</c> types is not covered by Trellis tests today and is therefore not
/// guaranteed to round-trip. Use <c>{ get; private set; }</c>, which is the supported shape.
/// The <c>TRLS022</c> analyzer flags <c>{ get; init; }</c> properties on <c>[OwnedEntity]</c> types.
/// </para>
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