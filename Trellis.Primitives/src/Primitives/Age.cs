namespace Trellis.Primitives;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// Age value object with validation for ages 0-150.
/// </summary>
/// <remarks>
/// <b>Validation Rules (Opinionated):</b>
/// <list type="bullet">
/// <item>Must be non-negative (>= 0)</item>
/// <item>Must be realistic (&lt;= 150)</item>
/// </list>
/// <para>
/// <b>If these rules don't fit your domain</b>, create your own Age value object
/// using the <see cref="ScalarValueObject{TSelf, T}"/> base class from the DomainDrivenDesign package.
/// </para>
/// </remarks>
[JsonConverter(typeof(ParsableJsonConverter<Age>))]
public class Age : ScalarValueObject<Age, int>, IScalarValue<Age, int>, IParsable<Age>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Age"/> class.
    /// </summary>
    private Age(int value) : base(value) { }

    /// <summary>
    /// Attempts to create an age.
    /// </summary>
    public static Result<Age> TryCreate(int value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Age) + '.' + nameof(TryCreate));
        var field = fieldName.NormalizeFieldName("age");
        if (value < 0)
            return Result.Failure<Age>(Error.Validation("Age must be non-negative.", field));
        if (value > 150)
            return Result.Failure<Age>(Error.Validation("Age is unrealistically high.", field));
        return new Age(value);
    }

    /// <summary>
    /// Attempts to create an <see cref="Age"/> from a string representation.
    /// </summary>
    public static Result<Age> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Age) + '.' + nameof(TryCreate));
        var field = fieldName.NormalizeFieldName("age");

        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<Age>(Error.Validation("Age is required.", field));

        if (!int.TryParse(value, out var parsed))
            return Result.Failure<Age>(Error.Validation("Age must be a valid integer.", field));

        return TryCreate(parsed, fieldName);
    }

    /// <summary>
    /// Parses an age.
    /// </summary>
    public static Age Parse(string? s, IFormatProvider? provider)
    {
        if (!int.TryParse(s, out var v))
            throw new FormatException("Value must be a valid integer.");
        var r = TryCreate(v);
        if (r.IsFailure)
        {
            var val = (ValidationError)r.Error;
            throw new FormatException(val.FieldErrors[0].Details[0]);
        }

        return r.Value;
    }

    /// <summary>
    /// Tries to parse an age.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Age result)
    {
        result = default;
        if (!int.TryParse(s, out var v))
            return false;
        var r = TryCreate(v);
        if (r.IsFailure)
            return false;

        result = r.Value;
        return true;
    }
}