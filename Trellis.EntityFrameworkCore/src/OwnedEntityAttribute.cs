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
/// <para>Properties should be settable so EF Core can populate them during materialization. Use <c>{ get; private set; }</c> as the supported pattern.</para>
/// <para>
/// <strong>Note — init-only properties.</strong> The supported shape is <c>{ get; private set; }</c>.
/// <c>{ get; init; }</c> on properties of <c>[OwnedEntity]</c> types is not currently exercised by
/// Trellis tests, and the generated private parameterless constructor pattern is designed around
/// post-construction assignment (which init setters do not allow). A future analyzer is planned
/// to explicitly enforce <c>private set;</c> on <c>[OwnedEntity]</c> properties at compile time.
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