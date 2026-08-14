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
///                    Error.InvalidInput.ForField("street", "invalid", "Street is required"))
///             .Ensure(x => !string.IsNullOrWhiteSpace(x.city),
///                    Error.InvalidInput.ForField("city", "invalid", "City is required"))
///             .Map(x => new Address(x.street, x.city, x.state, x.postalCode));
///
///     // Define what makes two addresses equal
///     protected override void GetEqualityComponents(ref EqualityComponents components)
///     {
///         components.Add(Street);
///         components.Add(City);
///         components.Add(State);
///         components.Add(PostalCode);
///     }
///     
///     // Domain behavior
///     public string GetFullAddress() => 
///         $"{Street}, {City}, {State} {PostalCode}";
/// }
/// 
/// // Usage
/// var address1 = Address.TryCreate("123 Main St", "Springfield", "IL", "62701")
///     .Match(a => a, e => throw new System.InvalidOperationException(e.GetDisplayMessage()));
/// var address2 = Address.TryCreate("123 Main St", "Springfield", "IL", "62701")
///     .Match(a => a, e => throw new System.InvalidOperationException(e.GetDisplayMessage()));
/// 
/// // Structural equality
/// address1 == address2; // true - same attributes
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
///             .Ensure(x => x.amount >= 0, Error.InvalidInput.ForField("amount", "invalid", "Amount cannot be negative"))
///             .Ensure(x => x.currency.Length == 3, 
///                    Error.InvalidInput.ForField("currency", "invalid", "Currency must be 3-letter ISO code"))
///             .Map(x => new Money(x.amount, x.currency.ToUpperInvariant()));
///     
///     protected override void GetEqualityComponents(ref EqualityComponents components)
///     {
///         components.Add(Amount);
///         components.Add(Currency);
///     }
///     
///     // Domain operations return new instances (immutability)
///     public Result&lt;Money&gt; Add(Money other) =>
///         Currency != other.Currency
///             ? Result.Fail&lt;Money&gt;(Error.InvalidInput.ForRule("currency_mismatch", $"Cannot add {other.Currency} to {Currency}"))
///             : Result.Ok(new Money(Amount + other.Amount, Currency));
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
///     protected override void GetEqualityComponents(ref EqualityComponents components)
///     {
///         base.GetEqualityComponents(ref components);
///         components.Add(Country);
///     }
/// }
/// </code>
/// </example>
public abstract class ValueObject : IComparable<ValueObject>, IComparable, IEquatable<ValueObject>
{
    // The lazily computed hash is published with a single plain int store, which the CLI spec
    // guarantees to be atomic, so a racing reader observes either UncomputedHashCode or the whole
    // value — never a torn one. Two threads may compute concurrently, but both derive the same
    // result from the same immutable components, so the duplicated work is benign and lock-free.
    private const int UncomputedHashCode = 0;

    // Sized so realistic value objects never touch the ArrayPool fallback in EqualityComponents.
    private const int InlineComponentCapacity = 8;

    private int _cachedHashCode;

    [System.Runtime.CompilerServices.InlineArray(InlineComponentCapacity)]
    private struct ComponentBuffer
    {
        private IComparable? element0;
    }

    /// <summary>
    /// When overridden in a derived class, adds the components that define equality for this value object.
    /// </summary>
    /// <param name="components">The sink that collects this value object's equality components.</param>
    /// <remarks>
    /// <para>
    /// This method is used by <see cref="Equals(ValueObject)"/>, <see cref="GetHashCode"/>, and
    /// <see cref="CompareTo(ValueObject?)"/> to determine equality and ordering. Components must be
    /// added in a consistent order.
    /// </para>
    /// <para>
    /// Guidelines:
    /// <list type="bullet">
    /// <item>Add all properties that define the value object's identity</item>
    /// <item>For derived classes, call <c>base.GetEqualityComponents(ref components)</c> first</item>
    /// <item>Add components in a consistent, deterministic order</item>
    /// <item>Do not allocate: the sink exists so comparisons stay allocation-free</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// protected override void GetEqualityComponents(ref EqualityComponents components)
    /// {
    ///     components.Add(Street);
    ///     components.Add(City);
    ///     components.Add(PostalCode);
    /// }
    /// </code>
    /// </example>
    protected abstract void GetEqualityComponents(ref EqualityComponents components);

    /// <summary>
    /// Converts a <see cref="Maybe{T}"/> to an <see cref="IComparable"/> for use in
    /// <see cref="GetEqualityComponents"/>. Returns the value if present, or <c>null</c> if empty.
    /// </summary>
    /// <typeparam name="T">The type of the optional value. Must implement <see cref="IComparable"/>.</typeparam>
    /// <param name="maybe">The optional value to convert.</param>
    /// <returns>The underlying value as <see cref="IComparable"/>, or <c>null</c> if the Maybe is empty.</returns>
    /// <remarks>
    /// Prefer <see cref="EqualityComponents.Add{T}(Maybe{T})"/>, which does the same conversion inline.
    /// </remarks>
    /// <example>
    /// <code>
    /// protected override void GetEqualityComponents(ref EqualityComponents components)
    /// {
    ///     components.Add(Street);
    ///     components.Add(MaybeComponent(Apartment));
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
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        if (GetType() != other.GetType())
            return false;

        // Two materialized, differing hashes prove inequality without touching components.
        var thisHash = _cachedHashCode;
        var otherHash = other._cachedHashCode;
        if (thisHash != UncomputedHashCode && otherHash != UncomputedHashCode && thisHash != otherHash)
            return false;

        ComponentBuffer leftStorage = default;
        ComponentBuffer rightStorage = default;
        var left = new EqualityComponents(leftStorage);
        var right = new EqualityComponents(rightStorage);

        try
        {
            GetEqualityComponents(ref left);
            other.GetEqualityComponents(ref right);
            return left.AsSpan().SequenceEqual(right.AsSpan());
        }
        finally
        {
            left.Return();
            right.Return();
        }
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
        var cached = _cachedHashCode;
        if (cached != UncomputedHashCode)
            return cached;

        ComponentBuffer storage = default;
        var components = new EqualityComponents(storage);
        int computed;

        try
        {
            GetEqualityComponents(ref components);

            computed = 1;
            foreach (var component in components.AsSpan())
                computed = HashCode.Combine(computed, component?.GetHashCode() ?? 0);
        }
        finally
        {
            components.Return();
        }

        // Remapping a genuine zero keeps it cacheable; the sentinel would otherwise force
        // recomputation on every call for those instances.
        if (computed == UncomputedHashCode)
            computed = 1;

        _cachedHashCode = computed;
        return computed;
    }

    /// <summary>
    /// Compares the current value object with another value object of the same type.
    /// </summary>
    /// <param name="other">The value object to compare with this instance.</param>
    /// <returns>
    /// A value less than zero if this instance is less than <paramref name="other"/>;
    /// zero if they are equal; or greater than zero if this instance is greater than <paramref name="other"/>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="other"/> is not of the same type as this instance.
    /// </exception>
    /// <remarks>
    /// Components are compared in order. The first non-equal component determines the result.
    /// Null sorts before non-null, so a non-null value object is greater than <c>null</c>.
    /// Components with different runtime types at the same position are ordered by type name before
    /// same-typed components use their native comparison. This enables value objects to be sorted
    /// and used in ordered collections.
    /// </remarks>
    public virtual int CompareTo(ValueObject? other)
    {
        if (ReferenceEquals(this, other))
            return 0;

        if (other is null)
            return 1;

        var thisType = GetType();
        var otherType = other.GetType();

        if (thisType != otherType)
            throw new ArgumentException($"Cannot compare objects of different types: {thisType} and {otherType}");

        ComponentBuffer leftStorage = default;
        ComponentBuffer rightStorage = default;
        var left = new EqualityComponents(leftStorage);
        var right = new EqualityComponents(rightStorage);

        try
        {
            GetEqualityComponents(ref left);
            other.GetEqualityComponents(ref right);

            var leftComponents = left.AsSpan();
            var rightComponents = right.AsSpan();
            var shared = Math.Min(leftComponents.Length, rightComponents.Length);

            for (var i = 0; i < shared; i++)
            {
                var comparison = CompareComponents(leftComponents[i], rightComponents[i]);
                if (comparison != 0)
                    return comparison;
            }

            // A prefix-equal shorter component list sorts first, matching enumeration order.
            return leftComponents.Length.CompareTo(rightComponents.Length);
        }
        finally
        {
            left.Return();
            right.Return();
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

    private static int CompareComponents(IComparable? object1, IComparable? object2)
    {
        if (object1 is null && object2 is null)
            return 0;

        if (object1 is null)
            return -1;

        if (object2 is null)
            return 1;

        var type1 = object1.GetType();
        var type2 = object2.GetType();

        if (type1 != type2)
        {
            var typeComparison = string.CompareOrdinal(type1.AssemblyQualifiedName, type2.AssemblyQualifiedName);
            return typeComparison;
        }

        return object1.CompareTo(object2);
    }

    private static int CompareNullable(ValueObject? left, ValueObject? right)
    {
        if (left is null && right is null)
            return 0;

        if (left is null)
            return -1;

        if (right is null)
            return 1;

        return left.CompareTo(right);
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
    /// <exception cref="ArgumentException">Thrown when both operands are non-null and have different runtime types.</exception>
    /// <remarks>
    /// This operator uses the same ordering as <see cref="CompareTo(ValueObject?)"/>: null sorts before non-null,
    /// and different runtime types are not comparable.
    /// </remarks>
    public static bool operator <(ValueObject? left, ValueObject? right) => CompareNullable(left, right) < 0;

    /// <summary>
    /// Determines whether the first value object is less than or equal to the second.
    /// </summary>
    /// <param name="left">The first value object to compare.</param>
    /// <param name="right">The second value object to compare.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is less than or equal to <paramref name="right"/>; otherwise, <c>false</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when both operands are non-null and have different runtime types.</exception>
    /// <remarks>
    /// This operator uses the same ordering as <see cref="CompareTo(ValueObject?)"/>: null sorts before non-null,
    /// and different runtime types are not comparable.
    /// </remarks>
    public static bool operator <=(ValueObject? left, ValueObject? right) => CompareNullable(left, right) <= 0;

    /// <summary>
    /// Determines whether the first value object is greater than the second.
    /// </summary>
    /// <param name="left">The first value object to compare.</param>
    /// <param name="right">The second value object to compare.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is greater than <paramref name="right"/>; otherwise, <c>false</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when both operands are non-null and have different runtime types.</exception>
    /// <remarks>
    /// This operator uses the same ordering as <see cref="CompareTo(ValueObject?)"/>: null sorts before non-null,
    /// and different runtime types are not comparable.
    /// </remarks>
    public static bool operator >(ValueObject? left, ValueObject? right) => CompareNullable(left, right) > 0;

    /// <summary>
    /// Determines whether the first value object is greater than or equal to the second.
    /// </summary>
    /// <param name="left">The first value object to compare.</param>
    /// <param name="right">The second value object to compare.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>; otherwise, <c>false</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when both operands are non-null and have different runtime types.</exception>
    /// <remarks>
    /// This operator uses the same ordering as <see cref="CompareTo(ValueObject?)"/>: null sorts before non-null,
    /// and different runtime types are not comparable.
    /// </remarks>
    public static bool operator >=(ValueObject? left, ValueObject? right) => CompareNullable(left, right) >= 0;
}
