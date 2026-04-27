namespace Trellis;

/// <summary>
/// Base class for value objects in Domain-Driven Design.
/// A value object represents a descriptive aspect of the domain with no conceptual identity.
/// Value objects are immutable, defined by their attributes, and support structural equality.
/// </summary>
/// <remarks>
/// <para>
/// Value objects are one of the three main building blocks in DDD (along with Entities and Aggregates).
/// Key characteristics:
/// <list type="bullet">
/// <item>Identity: Defined by attribute values, not by a unique identifier</item>
/// <item>Immutability: Once created, a value object's state cannot change</item>
/// <item>Equality: Two value objects with the same attributes are considered equal</item>
/// <item>Interchangeability: Value objects with equal attributes can be freely substituted</item>
/// <item>Side-effect free: Methods on value objects don't modify state, they return new instances</item>
/// </list>
/// </para>
/// <para>
/// Value Objects vs. Entities:
/// <list type="bullet">
/// <item><strong>Value Object</strong>: Defined by attributes (e.g., Address, Money, EmailAddress)</item>
/// <item><strong>Entity</strong>: Defined by identity (e.g., Customer, Order, Product)</item>
/// </list>
/// </para>
/// <para>
/// Benefits of using value objects:
/// <list type="bullet">
/// <item>Type safety: EmailAddress is more expressive than string</item>
/// <item>Validation: Encapsulate validation logic in the value object</item>
/// <item>Rich behavior: Add domain-specific methods (e.g., Money.Add, Temperature.ToFahrenheit)</item>
/// <item>Immutability: Prevents accidental state changes</item>
/// <item>Testability: Pure functions are easy to test</item>
/// </list>
/// </para>
/// <para>
/// When to use value objects:
/// <list type="bullet">
/// <item>The concept measures, quantifies, or describes something in the domain</item>
/// <item>It can be modeled as immutable</item>
/// <item>It models a conceptual whole by grouping related attributes</item>
/// <item>Equality should be based on the whole set of attributes</item>
/// <item>There's domain behavior associated with the concept</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Simple value object example:
/// <code>
/// public class Address : ValueObject
/// {
///     public string Street { get; }
///     public string City { get; }
///     public string State { get; }
///     public string PostalCode { get; }
///
///     private Address(string street, string city, string state, string postalCode)
///     {
///         Street = street;
///         City = city;
///         State = state;
///         PostalCode = postalCode;
///     }
///     
///     // Factory method with validation
///     public static Result&lt;Address&gt; TryCreate(
///         string street, string city, string state, string postalCode) =>
///         (street, city, state, postalCode).ToResult()
///             .Ensure(x => !string.IsNullOrWhiteSpace(x.street), 
///                    Error.UnprocessableContent.ForField("street", "invalid", "Street is required"))
///             .Ensure(x => !string.IsNullOrWhiteSpace(x.city),
///                    Error.UnprocessableContent.ForField("city", "invalid", "City is required"))
///             .Map(x => new Address(x.street, x.city, x.state, x.postalCode));
///
///     // Define what makes two addresses equal
///     protected override IEnumerable&lt;IComparable?&gt; GetEqualityComponents()
///     {
///         yield return Street;
///         yield return City;
///         yield return State;
///         yield return PostalCode;
///     }
///     
///     // Domain behavior
///     public string GetFullAddress() => 
///         $"{Street}, {City}, {State} {PostalCode}";
/// }
/// 
/// // Usage
/// var address1 = Address.TryCreate("123 Main St", "Springfield", "IL", "62701");
/// var address2 = Address.TryCreate("123 Main St", "Springfield", "IL", "62701");
/// 
/// // Structural equality
/// address1.Value == address2.Value; // true - same attributes
/// </code>
/// </example>
/// <example>
/// Value object with rich behavior:
/// <code>
/// public class Money : ValueObject
/// {
///     public decimal Amount { get; }
///     public string Currency { get; }
///     
///     private Money(decimal amount, string currency)
///     {
///         Amount = amount;
///         Currency = currency;
///     }
///     
///     public static Result&lt;Money&gt; TryCreate(decimal amount, string currency = "USD") =>
///         (amount, currency).ToResult()
///             .Ensure(x => x.amount >= 0, Error.UnprocessableContent.ForField("amount", "invalid", "Amount cannot be negative"))
///             .Ensure(x => x.currency.Length == 3, 
///                    Error.UnprocessableContent.ForField("currency", "invalid", "Currency must be 3-letter ISO code"))
///             .Map(x => new Money(x.amount, x.currency.ToUpperInvariant()));
///     
///     protected override IEnumerable&lt;IComparable?&gt; GetEqualityComponents()
///     {
///         yield return Amount;
///         yield return Currency;
///     }
///     
///     // Domain operations return new instances (immutability)
///     public Result&lt;Money&gt; Add(Money other) =>
///         Currency != other.Currency
///             ? Error.UnprocessableContent.ForRule("currency_mismatch", $"Cannot add {other.Currency} to {Currency}")
///             : new Money(Amount + other.Amount, Currency).ToResult();
///     
///     public Money Multiply(decimal factor) =>
///         new Money(Amount * factor, Currency);
/// }
/// </code>
/// </example>
/// <example>
/// Derived value object example:
/// <code>
/// public class InternationalAddress : Address
/// {
///     public string Country { get; }
///     
///     private InternationalAddress(
///         string street, string city, string state, 
///         string postalCode, string country) 
///         : base(street, city, state, postalCode)
///     {
///         Country = country;
///     }
///
///     // Include base components plus additional ones
///     protected override IEnumerable&lt;IComparable?&gt; GetEqualityComponents()
///     {
///         foreach (var component in base.GetEqualityComponents())
///             yield return component;
///         yield return Country;
///     }
/// }
/// </code>
/// </example>
public abstract class ValueObject : IComparable<ValueObject>, IComparable, IEquatable<ValueObject>
{
    // NOTE: Not volatile/locked intentionally. Value objects are immutable and DDD aggregates are
    // single-threaded consistency boundaries. The worst-case race is two threads computing the same
    // hash simultaneously and writing the same value — harmless on 64-bit CLR where int? writes are
    // atomic. Add Interlocked/volatile if value objects are ever used as keys in concurrent collections.
    private int? _cachedHashCode;

    /// <summary>
    /// When overridden in a derived class, returns the components that define equality for this value object.
    /// </summary>
    /// <returns>
    /// An enumerable of comparable objects that represent the value object's attributes.
    /// Two value objects are equal if their equality components are equal in the same order.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method is used by <see cref="Equals(ValueObject)"/> and <see cref="GetHashCode"/> to determine equality.
    /// Components should be returned in a consistent order.
    /// </para>
    /// <para>
    /// Guidelines:
    /// <list type="bullet">
    /// <item>Return all properties that define the value object's identity</item>
    /// <item>Use yield return for lazy evaluation</item>
    /// <item>For derived classes, include base.GetEqualityComponents() first</item>
    /// <item>Return components in a consistent, deterministic order</item>
    /// <item>Include all properties that should affect equality comparison</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// protected override IEnumerable&lt;IComparable?&gt; GetEqualityComponents()
    /// {
    ///     yield return Street;
    ///     yield return City;
    ///     yield return PostalCode;
    /// }
    /// </code>
    /// </example>
    protected abstract IEnumerable<IComparable?> GetEqualityComponents();

    /// <summary>
    /// Converts a <see cref="Maybe{T}"/> to an <see cref="IComparable"/> for use in
    /// <see cref="GetEqualityComponents"/>. Returns the value if present, or <c>null</c> if empty.
    /// </summary>
    /// <typeparam name="T">The type of the optional value. Must implement <see cref="IComparable"/>.</typeparam>
    /// <param name="maybe">The optional value to convert.</param>
    /// <returns>The underlying value as <see cref="IComparable"/>, or <c>null</c> if the Maybe is empty.</returns>
    /// <example>
    /// <code>
    /// protected override IEnumerable&lt;IComparable?&gt; GetEqualityComponents()
    /// {
    ///     yield return Street;
    ///     yield return MaybeComponent(Apartment);
    /// }
    /// </code>
    /// </example>
    protected static IComparable? MaybeComponent<T>(Maybe<T> maybe) where T : notnull, IComparable
        => maybe.HasValue ? maybe.Value : null;

    /// <summary>
    /// Determines whether the specified object is equal to the current value object.
    /// </summary>
    /// <param name="obj">The object to compare with the current value object.</param>
    /// <returns>
    /// <c>true</c> if the specified object is a value object of the same type with equal components;
    /// otherwise, <c>false</c>.
    /// </returns>
    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    /// <summary>
    /// Determines whether the specified value object is equal to the current value object.
    /// Two value objects are equal if they have the same type and all equality components are equal.
    /// </summary>
    /// <param name="other">The value object to compare with the current value object.</param>
    /// <returns>
    /// <c>true</c> if the value objects have the same type and equal components; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// This implements structural equality based on the components returned by <see cref="GetEqualityComponents"/>.
    /// Value objects of different types are never equal, even if they have the same component values.
    /// </remarks>
    public bool Equals(ValueObject? other)
    {
        if (other is null) return false;
        if (GetType() != other.GetType())
            return false;

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    /// <summary>
    /// Returns a hash code for this value object based on its equality components.
    /// </summary>
    /// <returns>A hash code combining all equality components.</returns>
    /// <remarks>
    /// The hash code is cached for performance since value objects are immutable.
    /// This ensures consistent hash codes for the lifetime of the object and improves
    /// performance when used as dictionary keys or in hash-based collections.
    /// </remarks>
    public override int GetHashCode()
    {
        if (!_cachedHashCode.HasValue)
        {
            _cachedHashCode = GetEqualityComponents()
                .Aggregate(1, (current, obj) => HashCode.Combine(current, obj?.GetHashCode() ?? 0));
        }

        return _cachedHashCode.Value;
    }

    /// <summary>
    /// Compares the current value object with another value object of the same type.
    /// </summary>
    /// <param name="other">The value object to compare with this instance.</param>
    /// <returns>
    /// A value less than zero if this instance is less than <paramref name="other"/>;
    /// zero if they are equal; or greater than zero if this instance is greater than <paramref name="other"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="other"/> is not of the same type as this instance.
    /// </exception>
    /// <remarks>
    /// Components are compared in order. The first non-equal component determines the result.
    /// This enables value objects to be sorted and used in ordered collections.
    /// </remarks>
    public virtual int CompareTo(ValueObject? other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var thisType = GetType();
        var otherType = other.GetType();

        if (thisType != otherType)
            throw new ArgumentException($"Cannot compare objects of different types: {thisType} and {otherType}");

        using var e1 = GetEqualityComponents().GetEnumerator();
        using var e2 = other.GetEqualityComponents().GetEnumerator();

        while (true)
        {
            var has1 = e1.MoveNext();
            var has2 = e2.MoveNext();

            if (!has1 && !has2)
                return 0;
            if (!has1)
                return -1;
            if (!has2)
                return 1;

            var comparison = CompareComponents(e1.Current, e2.Current);
            if (comparison != 0)
                return comparison;
        }
    }

    /// <summary>
    /// Non-generic <see cref="IComparable"/> implementation. Delegates to <see cref="CompareTo(ValueObject?)"/>.
    /// Enables value objects to be used in <see cref="ValueObject.GetEqualityComponents"/> of composite value objects.
    /// </summary>
    int IComparable.CompareTo(object? obj) => obj switch
    {
        null => 1,
        ValueObject other => CompareTo(other),
        _ => throw new ArgumentException($"Cannot compare {GetType()} to {obj.GetType()}")
    };

    private static int CompareComponents(object? object1, object? object2)
    {
        if (object1 is null && object2 is null)
            return 0;

        if (object1 is null)
            return -1;

        if (object2 is null)
            return 1;

        if (object1 is IComparable comparable1 && object2 is IComparable comparable2)
            return comparable1.CompareTo(comparable2);

        return object1.Equals(object2) ? 0 : -1;
    }

    /// <summary>
    /// Determines whether two value objects are equal.
    /// </summary>
    /// <param name="a">The first value object to compare.</param>
    /// <param name="b">The second value object to compare.</param>
    /// <returns><c>true</c> if both are null or have equal components; otherwise, <c>false</c>.</returns>
    public static bool operator ==(ValueObject? a, ValueObject? b)
    {
        if (a is null && b is null)
            return true;

        if (a is null || b is null)
            return false;

        return a.Equals(b);
    }

    /// <summary>
    /// Determines whether two value objects are not equal.
    /// </summary>
    /// <param name="a">The first value object to compare.</param>
    /// <param name="b">The second value object to compare.</param>
    /// <returns><c>true</c> if the value objects have different components; otherwise, <c>false</c>.</returns>
    public static bool operator !=(ValueObject? a, ValueObject? b) => !(a == b);

    /// <summary>
    /// Determines whether the first value object is less than the second.
    /// </summary>
    /// <param name="left">The first value object to compare.</param>
    /// <param name="right">The second value object to compare.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is less than <paramref name="right"/>; otherwise, <c>false</c>.</returns>
    public static bool operator <(ValueObject? left, ValueObject? right)
    {
        if (left is null || right is null) return false;
        if (left.GetType() != right.GetType()) return false;
        return left.CompareTo(right) < 0;
    }

    /// <summary>
    /// Determines whether the first value object is less than or equal to the second.
    /// </summary>
    /// <param name="left">The first value object to compare.</param>
    /// <param name="right">The second value object to compare.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is less than or equal to <paramref name="right"/>; otherwise, <c>false</c>.</returns>
    public static bool operator <=(ValueObject? left, ValueObject? right)
    {
        if (left is null || right is null) return left is null && right is null;
        if (left.GetType() != right.GetType()) return false;
        return left.CompareTo(right) <= 0;
    }

    /// <summary>
    /// Determines whether the first value object is greater than the second.
    /// </summary>
    /// <param name="left">The first value object to compare.</param>
    /// <param name="right">The second value object to compare.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is greater than <paramref name="right"/>; otherwise, <c>false</c>.</returns>
    public static bool operator >(ValueObject? left, ValueObject? right)
    {
        if (left is null || right is null) return false;
        if (left.GetType() != right.GetType()) return false;
        return left.CompareTo(right) > 0;
    }

    /// <summary>
    /// Determines whether the first value object is greater than or equal to the second.
    /// </summary>
    /// <param name="left">The first value object to compare.</param>
    /// <param name="right">The second value object to compare.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>; otherwise, <c>false</c>.</returns>
    public static bool operator >=(ValueObject? left, ValueObject? right)
    {
        if (left is null || right is null) return left is null && right is null;
        if (left.GetType() != right.GetType()) return false;
        return left.CompareTo(right) >= 0;
    }
}