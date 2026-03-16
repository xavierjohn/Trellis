namespace Trellis;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

/// <summary>
/// Base class for creating strongly-typed, behavior-rich enumeration value objects.
/// Enum value objects are a DDD pattern that replaces C# enums with full-featured classes.
/// </summary>
/// <typeparam name="TSelf">The derived enum value object type itself (CRTP pattern).</typeparam>
/// <remarks>
/// <para>
/// Enum value objects address limitations of C# enums:
/// <list type="bullet">
/// <item><strong>Behavior</strong>: Each value can have associated behavior and properties</item>
/// <item><strong>Type safety</strong>: Invalid values are impossible (no <c>(OrderStatus)999</c>)</item>
/// <item><strong>Extensibility</strong>: Add methods, computed properties, and domain logic</item>
/// <item><strong>State machines</strong>: Model valid transitions between states</item>
/// </list>
/// </para>
/// <para>
/// Each enum value object member is defined as a static readonly field:
/// <list type="bullet">
/// <item>Members are discovered via reflection and cached for performance</item>
/// <item>The <see cref="Name"/> property is auto-derived from the field name (infrastructure concern)</item>
/// <item>The <see cref="Value"/> property is auto-generated for persistence (infrastructure concern)</item>
/// </list>
/// </para>
/// <para>
/// When used with the <c>partial</c> keyword, the PrimitiveValueObjectGenerator source generator
/// automatically creates:
/// <list type="bullet">
/// <item><c>IScalarValue&lt;TSelf, string&gt;</c> implementation for ASP.NET Core automatic validation</item>
/// <item><c>TryCreate(string)</c> - Factory method for non-nullable strings (required by IScalarValue)</item>
/// <item><c>TryCreate(string?, string?)</c> - Factory method with validation and custom field name</item>
/// <item><c>IParsable&lt;T&gt;</c> implementation (<c>Parse</c>, <c>TryParse</c>)</item>
/// <item>JSON serialization support via <c>RequiredEnumJsonConverter&lt;T&gt;</c></item>
/// <item>ASP.NET Core model binding from route/query/form/headers</item>
/// <item>OpenTelemetry activity tracing</item>
/// </list>
/// </para>
/// <para>
/// Common use cases:
/// <list type="bullet">
/// <item>Order/payment/shipping statuses</item>
/// <item>User roles and permissions</item>
/// <item>Document states in workflows</item>
/// <item>Any finite set of domain values with behavior</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Basic enum value object:
/// <code><![CDATA[
/// public partial class OrderState : RequiredEnum<OrderState>
/// {
///     public static readonly OrderState Draft = new();
///     public static readonly OrderState Confirmed = new();
///     public static readonly OrderState Shipped = new();
///     public static readonly OrderState Delivered = new();
///     public static readonly OrderState Cancelled = new();
/// }
/// 
/// // The source generator automatically creates:
/// // - IScalarValue<OrderState, string> interface implementation
/// // - public static Result<OrderState> TryCreate(string value)
/// // - public static Result<OrderState> TryCreate(string? value, string? fieldName = null)
/// // - public static OrderState Parse(string s, IFormatProvider? provider)
/// // - public static bool TryParse(string? s, IFormatProvider? provider, out OrderState result)
/// // - [JsonConverter(typeof(RequiredEnumJsonConverter<OrderState>))] attribute
/// 
/// // Usage - Name is auto-derived from field name
/// var state = OrderState.Draft;           // Name = "Draft"
/// var all = OrderState.GetAll();
/// var result = OrderState.TryCreate("Draft");  // Result<OrderState>
/// ]]></code>
/// </example>
/// <example>
/// Enum value object with behavior:
/// <code><![CDATA[
/// public partial class PaymentMethod : RequiredEnum<PaymentMethod>
/// {
///     public static readonly PaymentMethod CreditCard = new(fee: 0.029m);
///     public static readonly PaymentMethod BankTransfer = new(fee: 0.005m);
///     public static readonly PaymentMethod Cash = new(fee: 0m);
///     
///     public decimal Fee { get; }
///     
///     private PaymentMethod(decimal fee) => Fee = fee;
///     
///     public decimal CalculateFee(decimal amount) => amount * Fee;
/// }
/// ]]></code>
/// </example>
/// <example>
/// Using in ASP.NET Core DTOs with automatic validation:
/// <code><![CDATA[
/// public record UpdateOrderDto
/// {
///     public OrderState State { get; init; } = null!;
/// }
/// 
/// // In controller - validation happens automatically
/// [HttpPut("{id}")]
/// public IActionResult UpdateOrder(Guid id, UpdateOrderDto dto)
/// {
///     // If we reach here, dto.State is already validated!
///     return Ok(_orderService.UpdateState(id, dto.State));
/// }
/// ]]></code>
/// </example>
/// <remarks>
/// <para>
/// <strong>Note on IScalarValue implementation:</strong> This base class requires <c>TSelf</c> to implement
/// <see cref="IScalarValue{TSelf, TPrimitive}"/> via the constraint <c>where TSelf : IScalarValue&lt;TSelf, string&gt;</c>.
/// The actual interface implementation (including the <c>static abstract TryCreate</c> method and <c>Value</c> property)
/// is provided by the source generator on each concrete derived class.
/// </para>
/// <para>
/// The source generator adds:
/// <list type="bullet">
/// <item><c>IScalarValue&lt;TSelf, string&gt;</c> interface declaration</item>
/// <item>Explicit implementation of <c>string IScalarValue&lt;TSelf, string&gt;.Value =&gt; Name;</c></item>
/// <item><c>TryCreate(string)</c> and <c>TryCreate(string?, string?)</c> methods (required by IScalarValue)</item>
/// <item><c>IParsable&lt;TSelf&gt;</c> implementation</item>
/// <item><c>[JsonConverter]</c> attribute</item>
/// </list>
/// </para>
/// </remarks>
#pragma warning disable CA1000 // Do not declare static members on generic types - required for factory pattern
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix - RequiredEnum is a valid DDD pattern name
public abstract class RequiredEnum<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TSelf>
    : IEquatable<RequiredEnum<TSelf>>
    where TSelf : RequiredEnum<TSelf>, IScalarValue<TSelf, string>
#pragma warning restore CA1711
{
    private static readonly ConcurrentDictionary<Type, (ReadOnlyCollection<TSelf> Members, Dictionary<string, TSelf> ByName)> s_cache = new();

    /// <summary>
    /// Gets the name of this enum value object member.
    /// Auto-derived from the field name during discovery.
    /// This is an infrastructure concern for serialization and display.
    /// </summary>
    /// <remarks>
    /// Name is lazily initialized on first access to avoid chicken-and-egg issues
    /// with static field initialization order.
    /// </remarks>
    public string Name => _name ?? InitializeName();

    private string? _name;

    private string InitializeName()
    {
        _ = GetCache(); // Populates _name
        return _name!;
    }

    /// <summary>
    /// Gets the auto-generated integer value for persistence.
    /// This is an infrastructure concern - values are assigned based on declaration order (0, 1, 2, ...).
    /// </summary>
    /// <remarks>
    /// Value is lazily initialized on first access to avoid chicken-and-egg issues
    /// with static field initialization order.
    /// </remarks>
    public int Value => _value ?? InitializeValue();

    private int? _value;

    private int InitializeValue()
    {
        _ = GetCache(); // Populates _value
        return _value!.Value;
    }

    /// <summary>
    /// Initializes a new instance. The Name is auto-derived from the field name.
    /// </summary>
    protected RequiredEnum()
    {
        // Name and Value are set during discovery via reflection
    }

    /// <summary>
    /// Gets all defined members of this enum value object type.
    /// </summary>
    public static IReadOnlyCollection<TSelf> GetAll() => GetCache().Members;

    /// <summary>
    /// Attempts to find a member by its name (case-insensitive).
    /// </summary>
    /// <param name="name">The name to search for.</param>
    /// <param name="fieldName">Optional field name for validation error messages.</param>
    /// <returns>A <see cref="Result{TSelf}"/> containing the matching member or a validation error.</returns>
    public static Result<TSelf> TryFromName(string? name, string? fieldName = null)
    {
        var field = NormalizeFieldName(fieldName, typeof(TSelf).Name);

        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation($"{typeof(TSelf).Name} cannot be empty.", field);

        var cache = GetCache();
        if (cache.ByName.TryGetValue(name, out var member))
            return member;

        var validNames = string.Join(", ", cache.ByName.Keys.OrderBy(n => n));
        return Error.Validation($"'{name}' is not a valid {typeof(TSelf).Name}. Valid values: {validNames}", field);
    }

    /// <summary>
    /// Checks if this instance is one of the specified values.
    /// </summary>
    public bool Is(params TSelf[] values) => values.Contains((TSelf)this);

    /// <summary>
    /// Checks if this instance is not one of the specified values.
    /// </summary>
    public bool IsNot(params TSelf[] values) => !Is(values);

    /// <inheritdoc />
    public override string ToString() => Name;

    /// <inheritdoc />
    public override int GetHashCode() => Name.GetHashCode(StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RequiredEnum<TSelf> other && Equals(other);

    /// <inheritdoc />
    public bool Equals(RequiredEnum<TSelf>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Determines whether two instances are equal.</summary>
    public static bool operator ==(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Determines whether two instances are not equal.</summary>
    public static bool operator !=(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right) => !(left == right);

    private static (ReadOnlyCollection<TSelf> Members, Dictionary<string, TSelf> ByName) GetCache() =>
        s_cache.GetOrAdd(typeof(TSelf), _ =>
        {
            var members = DiscoverMembers().ToList();
            var byName = members.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
            return (members.AsReadOnly(), byName);
        });

    private static IEnumerable<TSelf> DiscoverMembers()
    {
        var fields = typeof(TSelf).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var index = 0;

        foreach (var field in fields)
        {
            if (field.FieldType == typeof(TSelf) && field.IsInitOnly && field.GetValue(null) is TSelf member)
            {
                // Auto-derive Name from field name and assign Value
                member._name = field.Name;
                member._value = index++;
                yield return member;
            }
        }
    }

    private static string NormalizeFieldName(string? fieldName, string typeName) =>
        !string.IsNullOrEmpty(fieldName)
            ? (fieldName.Length == 1 ? fieldName.ToLowerInvariant() : char.ToLowerInvariant(fieldName[0]) + fieldName[1..])
            : char.ToLowerInvariant(typeName[0]) + typeName[1..];
}
#pragma warning restore CA1000