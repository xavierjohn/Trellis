namespace Trellis;

/// <summary>
/// Base class for entities in Domain-Driven Design.
/// An entity is a domain object defined by its identity rather than its attributes.
/// Two entities with different attributes but the same ID are considered equal.
/// </summary>
/// <typeparam name="TId">The type of the entity's unique identifier. Must be non-nullable.</typeparam>
/// <remarks>
/// <para>
/// Entities are one of the three main building blocks in DDD (along with Value Objects and Aggregates).
/// Key characteristics:
/// <list type="bullet">
/// <item>Identity: Each entity has a unique identifier that remains constant throughout its lifetime</item>
/// <item>Mutability: Entity state can change over time, but identity remains the same</item>
/// <item>Equality: Two entities are equal if they have the same ID, regardless of attribute values</item>
/// <item>Lifecycle: Entities have a continuous identity thread through state changes</item>
/// </list>
/// </para>
/// <para>
/// Entities vs. Value Objects:
/// <list type="bullet">
/// <item><strong>Entity</strong>: Defined by identity (e.g., Customer, Order, Product)</item>
/// <item><strong>Value Object</strong>: Defined by attributes (e.g., Address, Money, EmailAddress)</item>
/// </list>
/// </para>
/// <para>
/// Identity best practices:
/// <list type="bullet">
/// <item>Use strongly-typed IDs (e.g., CustomerId, OrderId) instead of primitives</item>
/// <item>Generate IDs at creation time, typically using NewUnique() or from external source</item>
/// <item>Make ID immutable using init-only setter</item>
/// <item>Never expose ID setters or allow ID changes after creation</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Define a strongly-typed ID
/// public class CustomerId : ScalarValueObject&lt;Guid&gt;
/// {
///     private CustomerId(Guid value) : base(value) { }
///     public static CustomerId NewUnique() => new(Guid.NewGuid());
/// }
/// 
/// // Define an entity
/// public class Customer : Entity&lt;CustomerId&gt;
/// {
///     public string Name { get; private set; }
///     public EmailAddress Email { get; private set; }
///     
///     // CreatedAt and LastModified are inherited from Entity&lt;TId&gt;
///     // and automatically managed by EntityTimestampInterceptor.
///     
///     private Customer(CustomerId id, string name, EmailAddress email)
///         : base(id)
///     {
///         Name = name;
///         Email = email;
///     }
///     
///     public static Result&lt;Customer&gt; TryCreate(string name, EmailAddress email) =>
///         name.ToResult()
///             .Ensure(n => !string.IsNullOrWhiteSpace(n), Error.UnprocessableContent.ForField("name", "invalid", "Name required"))
///             .Map(n => new Customer(CustomerId.NewUnique(), n, email));
///     
///     public Result&lt;Customer&gt; UpdateEmail(EmailAddress newEmail) =>
///         newEmail.ToResult()
///             .Tap(e => Email = e)
///             .Map(_ => this);
/// }
/// 
/// // Usage - identity-based equality
/// var customer1 = Customer.Create("John", email);
/// var customer2 = Customer.Create("John", email);
/// 
/// // Different instances with same data but different IDs
/// customer1 != customer2; // true - different identities
/// 
/// // Same instance
/// var sameCustomer = customer1;
/// customer1 == sameCustomer; // true - same identity
/// </code>
/// </example>
public abstract class Entity<TId> : IEntity
    where TId : notnull
{
    /// <summary>
    /// Gets the unique identifier of this entity.
    /// The identifier is immutable and defines the entity's identity.
    /// </summary>
    /// <value>
    /// The unique identifier that distinguishes this entity from all others of the same type.
    /// </value>
    /// <remarks>
    /// The ID should be:
    /// <list type="bullet">
    /// <item>Set at construction time and never changed</item>
    /// <item>Non-null and not equal to default(TId)</item>
    /// <item>Unique within the entity type's scope</item>
    /// <item>Preferably a strongly-typed value object (e.g., CustomerId, OrderId)</item>
    /// </list>
    /// </remarks>
    public TId Id { get; init; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this entity was first persisted.
    /// Automatically managed by <c>EntityTimestampInterceptor</c>.
    /// </summary>
    /// <value>
    /// Defaults to <c>default(DateTimeOffset)</c> for unsaved entities.
    /// Set once on first save; not updated on subsequent modifications.
    /// </value>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp of the most recent modification.
    /// Automatically managed by <c>EntityTimestampInterceptor</c>.
    /// </summary>
    /// <value>
    /// Defaults to <c>default(DateTimeOffset)</c> for unsaved entities.
    /// Updated on every save (add or modify).
    /// </value>
    public DateTimeOffset LastModified { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Entity{TId}"/> class with the specified identifier.
    /// </summary>
    /// <param name="id">The unique identifier for this entity. Must not be null or default.</param>
    /// <remarks>
    /// This constructor should be called by derived classes to set the entity's identity.
    /// The ID should typically be generated using a NewUnique() method or provided from an external source.
    /// </remarks>
    protected Entity(TId id) => Id = id;

    /// <summary>
    /// Determines whether the specified object is equal to the current entity.
    /// Two entities are equal if they have the same type and the same non-default ID.
    /// </summary>
    /// <param name="obj">The object to compare with the current entity.</param>
    /// <returns>
    /// <c>true</c> if the specified object is an entity of the same type with the same non-default ID;
    /// otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method implements identity-based equality, which is fundamental to entities in DDD.
    /// The method returns <c>false</c> if:
    /// <list type="bullet">
    /// <item>The compared object is not an entity of the same type</item>
    /// <item>Either entity has a null or default ID (transient entities)</item>
    /// <item>The IDs are different</item>
    /// </list>
    /// </para>
    /// <para>
    /// Transient entities (those with null or default IDs) are never equal to other entities,
    /// unless they are the same instance (reference equality).
    /// </para>
    /// </remarks>
    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        // Different entity types are never equal, even if they share the same ID type
        if (GetType() != other.GetType())
            return false;

        if (Id is null || Id.Equals(default(TId)) || other.Id is null || other.Id.Equals(default(TId)))
            return false;

        return Id.Equals(other.Id);
    }

    /// <summary>
    /// Determines whether two entities are equal using identity-based comparison.
    /// </summary>
    /// <param name="a">The first entity to compare.</param>
    /// <param name="b">The second entity to compare.</param>
    /// <returns><c>true</c> if both entities are null, or if both have the same non-default ID; otherwise, <c>false</c>.</returns>
    public static bool operator ==(Entity<TId>? a, Entity<TId>? b)
    {
        if (a is null && b is null)
            return true;

        if (a is null || b is null)
            return false;

        return a.Equals(b);
    }

    /// <summary>
    /// Determines whether two entities are not equal using identity-based comparison.
    /// </summary>
    /// <param name="a">The first entity to compare.</param>
    /// <param name="b">The second entity to compare.</param>
    /// <returns><c>true</c> if the entities have different IDs or one is null; otherwise, <c>false</c>.</returns>
    public static bool operator !=(Entity<TId>? a, Entity<TId>? b) => !(a == b);

    /// <summary>
    /// Returns a hash code for this entity based on its type and ID.
    /// </summary>
    /// <returns>A hash code combining the entity type and its identifier.</returns>
    /// <remarks>
    /// The hash code is based on both the entity type and its ID to ensure proper behavior in collections.
    /// Entities with the same ID but different types will have different hash codes.
    /// </remarks>
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}