namespace Trellis;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
/// <item>The <see cref="Value"/> property is the semantic string value. It defaults to the field name and can be overridden with <see cref="EnumValueAttribute"/> only when the external name must differ.</item>
/// <item>The <see cref="Ordinal"/> property is secondary declaration-order metadata, not semantic identity</item>
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
/// // Usage - Value defaults to the field name
/// var state = OrderState.Draft;           // Value = "Draft"
/// var all = OrderState.GetAll();
/// var result = OrderState.TryCreate("Draft");  // Result<OrderState>
/// ]]></code>
/// </example>
/// <example>
/// Enum value object with behavior, using field names by default and an override only where needed:
/// <code><![CDATA[
/// public partial class PaymentMethod : RequiredEnum<PaymentMethod>
/// {
///     public static readonly PaymentMethod CreditCard = new(fee: 0.029m);
///     public static readonly PaymentMethod BankTransfer = new(fee: 0.005m);
///     [EnumValue("cash-payment")]
///     public static readonly PaymentMethod Cash = new(fee: 0m);
///     
///     public decimal Fee { get; }
///     
///     private PaymentMethod(decimal fee) => Fee = fee;
///     
///     public decimal CalculateFee(decimal amount) => amount * Fee;
/// }
/// 
/// // CreditCard.Value == "CreditCard"
/// // Cash.Value == "cash-payment"
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
/// <item><c>TryCreate(string)</c> and <c>TryCreate(string?, string?)</c> methods (required by IScalarValue)</item>
/// <item><c>IParsable&lt;TSelf&gt;</c> implementation</item>
/// <item><c>[JsonConverter]</c> attribute</item>
/// </list>
/// </para>
/// </remarks>
#pragma warning disable CA1000 // Do not declare static members on generic types - required for factory pattern
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix - RequiredEnum is a valid DDD pattern name
[DebuggerDisplay("{Value}")]
public abstract class RequiredEnum<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TSelf>
    : IEquatable<RequiredEnum<TSelf>>, IComparable<RequiredEnum<TSelf>>, IComparable
    where TSelf : RequiredEnum<TSelf>, IScalarValue<TSelf, string>
#pragma warning restore CA1711
{
    private static readonly ConcurrentDictionary<Type, (ReadOnlyCollection<TSelf> Members, Dictionary<string, TSelf> ByName)> s_cache = new();

    /// <summary>
    /// Gets the string value of this enum value object member.
    /// Defaults to the field name during discovery and can be overridden with <see cref="EnumValueAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Value is lazily initialized on first access to avoid chicken-and-egg issues
    /// with static field initialization order.
    /// </remarks>
    public string Value => _value ?? InitializeValue();

    private string? _value;

    private string InitializeValue()
    {
        _ = GetCache(); // Populates _value
        return _value!;
    }

    /// <summary>
    /// Gets auto-generated declaration-order metadata for the member.
    /// This is a secondary infrastructure/detail value, not semantic identity.
    /// </summary>
    /// <remarks>
    /// Ordinal values are assigned from declaration order (0, 1, 2, ...).
    /// Reordering fields changes ordinals, so they should not be treated as stable wire or storage contracts.
    /// <para>
    /// Ordinal is lazily initialized on first access to avoid chicken-and-egg issues
    /// with static field initialization order.
    /// </para>
    /// </remarks>
    public int Ordinal => _ordinal ?? InitializeOrdinal();

    private int? _ordinal;

    private int InitializeOrdinal()
    {
        _ = GetCache(); // Populates _ordinal
        return _ordinal!.Value;
    }

    /// <summary>
    /// Initializes a new instance. The symbolic value is assigned during member discovery.
    /// </summary>
    protected RequiredEnum()
    {
        // Value and Ordinal are set during discovery via reflection
    }

    /// <summary>
    /// Gets all defined members of this enum value object type.
    /// </summary>
    public static IReadOnlyCollection<TSelf> GetAll() => GetCache().Members;

    /// <summary>
    /// Creates a validated member from its symbolic value (case-insensitive). This is the public
    /// factory every <c>Required*</c> primitive exposes; the JSON converter, EF converter, and
    /// <c>Parse</c>/<c>TryParse</c> all route through it. Provided by the base because an enum's
    /// creation is a uniform name lookup, so — unlike the scalar primitives — it needs no
    /// per-derived-type generation.
    /// </summary>
    /// <param name="value">The symbolic value to look up.</param>
    /// <returns>A <see cref="Result{TSelf}"/> containing the matching member or a validation error.</returns>
    public static Result<TSelf> TryCreate(string value) => TryCreate(value, null);

    /// <summary>
    /// Creates a validated member from its symbolic value (case-insensitive), reporting validation
    /// failures against <paramref name="fieldName"/>.
    /// </summary>
    /// <param name="value">The symbolic value to look up.</param>
    /// <param name="fieldName">Optional field name for validation error messages.</param>
    /// <returns>A <see cref="Result{TSelf}"/> containing the matching member or a validation error.</returns>
    public static Result<TSelf> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity($"{typeof(TSelf).Name}.TryCreate");

        var field = NormalizeFieldName(fieldName, typeof(TSelf).Name);

        if (string.IsNullOrWhiteSpace(value))
            return Result.Fail<TSelf>(Error.InvalidInput.ForField(field, "validation.error", $"{typeof(TSelf).Name} cannot be empty."));

        var cache = GetCache();
        if (cache.ByName.TryGetValue(value, out var member))
            return Result.Ok(member);

        var validNames = string.Join(", ", cache.ByName.Keys.OrderBy(n => n, StringComparer.Ordinal));
        return Result.Fail<TSelf>(Error.InvalidInput.ForField(field, "validation.error", $"'{value}' is not a valid {typeof(TSelf).Name}. Valid values: {validNames}"));
    }

    /// <summary>
    /// Checks if this instance is one of the specified values.
    /// </summary>
    /// <param name="values">The values to compare against.</param>
    /// <returns><c>true</c> if this instance matches any of the specified values; otherwise, <c>false</c>.</returns>
    public bool Is(params TSelf[] values) => values.Contains((TSelf)this);

    /// <summary>
    /// Checks if this instance is not one of the specified values.
    /// </summary>
    /// <param name="values">The values to compare against.</param>
    /// <returns><c>true</c> if this instance does not match any of the specified values; otherwise, <c>false</c>.</returns>
    public bool IsNot(params TSelf[] values) => !Is(values);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode(StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RequiredEnum<TSelf> other && Equals(other);

    /// <inheritdoc />
    public bool Equals(RequiredEnum<TSelf>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Determines whether two instances are equal.</summary>
    public static bool operator ==(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Determines whether two instances are not equal.</summary>
    public static bool operator !=(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right) => !(left == right);

    /// <summary>
    /// Compares this instance with another by <see cref="Ordinal"/> (declaration order), so members
    /// sort in the order they are declared — matching how the C# <see langword="enum"/> this type
    /// replaces would sort by its underlying value. Equality remains keyed on <see cref="Value"/>;
    /// the two stay consistent because each member has a unique <see cref="Value"/> and a unique
    /// <see cref="Ordinal"/>.
    /// </summary>
    /// <param name="other">The instance to compare with, or <see langword="null"/>.</param>
    /// <returns>
    /// A negative value if this instance precedes <paramref name="other"/>; zero if they are equal;
    /// a positive value if this instance follows <paramref name="other"/> or <paramref name="other"/> is <see langword="null"/>.
    /// </returns>
    public int CompareTo(RequiredEnum<TSelf>? other)
    {
        if (other is null) return 1;
        if (ReferenceEquals(this, other)) return 0;

        return Ordinal.CompareTo(other.Ordinal);
    }

    /// <summary>
    /// Non-generic <see cref="IComparable"/> implementation. Enables members to be used as
    /// <see cref="ValueObject.GetEqualityComponents"/> components of composite value objects and to
    /// be ordered by the default comparer.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>The relative order, as described by <see cref="CompareTo(RequiredEnum{TSelf})"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="obj"/> is non-null and not a <see cref="RequiredEnum{TSelf}"/> (consistent with <see cref="Equals(object?)"/>).</exception>
    int IComparable.CompareTo(object? obj) => obj switch
    {
        null => 1,
        RequiredEnum<TSelf> other => CompareTo(other),
        _ => throw new ArgumentException($"Cannot compare {GetType()} to {obj.GetType()}.", nameof(obj)),
    };

    /// <summary>Determines whether the left instance precedes the right in declaration order (<see cref="Ordinal"/>).</summary>
    public static bool operator <(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right) =>
        left is null ? right is not null : left.CompareTo(right) < 0;

    /// <summary>Determines whether the left instance precedes or equals the right in declaration order (<see cref="Ordinal"/>).</summary>
    public static bool operator <=(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right) =>
        left is null || left.CompareTo(right) <= 0;

    /// <summary>Determines whether the left instance follows the right in declaration order (<see cref="Ordinal"/>).</summary>
    public static bool operator >(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right) =>
        left is not null && left.CompareTo(right) > 0;

    /// <summary>Determines whether the left instance follows or equals the right in declaration order (<see cref="Ordinal"/>).</summary>
    public static bool operator >=(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right) =>
        left is null ? right is null : left.CompareTo(right) >= 0;

    private static (ReadOnlyCollection<TSelf> Members, Dictionary<string, TSelf> ByName) GetCache() =>
        s_cache.GetOrAdd(typeof(TSelf), _ =>
        {
            var members = DiscoverMembers().ToList();
            var duplicateValue = members
                .GroupBy(member => member.Value, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);

            if (duplicateValue is not null)
                throw new InvalidOperationException(
                    $"RequiredEnum '{typeof(TSelf).Name}' contains duplicate symbolic value '{duplicateValue.Key}'. " +
                    "Each member must have a unique Value.");

            var byName = members.ToDictionary(m => m.Value, StringComparer.OrdinalIgnoreCase);
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
                var enumValue = field.GetCustomAttribute<EnumValueAttribute>()?.Value;

                // Assign the semantic symbolic value and declaration order.
                member._value = enumValue ?? field.Name;
                member._ordinal = index++;
                yield return member;
            }
        }
    }

    private static string NormalizeFieldName(string? fieldName, string typeName) =>
        fieldName.NormalizeFieldName(typeName.ToCamelCase());
}
#pragma warning restore CA1000