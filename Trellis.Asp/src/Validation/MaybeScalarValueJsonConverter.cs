using Trellis.Asp;

namespace Trellis.Asp.Validation;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// A JSON converter for <see cref="Maybe{TValue}"/> properties where <typeparamref name="TValue"/>
/// implements <see cref="IScalarValue{TSelf, TPrimitive}"/>.
/// </summary>
/// <typeparam name="TValue">The scalar value object type.</typeparam>
/// <typeparam name="TPrimitive">The underlying primitive type.</typeparam>
/// <remarks>
/// <para>
/// This converter enables DTOs to use <see cref="Maybe{TValue}"/> for optional value object properties:
/// </para>
/// <list type="bullet">
/// <item><c>null</c> JSON value → <c>Maybe.None&lt;TValue&gt;()</c> — no error, value was not provided</item>
/// <item>Valid JSON value → <c>Maybe.From(validated)</c> — value provided and valid</item>
/// <item>Invalid JSON value → validation error collected, continues deserialization</item>
/// </list>
/// <para>
/// Unlike <see cref="ValidatingJsonConverter{TValue, TPrimitive}"/>, this converter treats
/// JSON <c>null</c> as a valid "not provided" signal rather than an error.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public record RegisterUserDto
/// {
///     public EmailAddress Email { get; init; } = null!;       // required
///     public Maybe&lt;FirstName&gt; FirstName { get; init; }  // optional — null = None
/// }
/// </code>
/// </example>
public sealed class MaybeScalarValueJsonConverter<TValue, TPrimitive> : ScalarValueJsonConverterBase<Maybe<TValue>, TValue, TPrimitive>
    where TValue : class, IScalarValue<TValue, TPrimitive>
    where TPrimitive : IComparable
{
    /// <inheritdoc />
    protected override Maybe<TValue> OnNullToken(string fieldName) => default;

    /// <inheritdoc />
    protected override Maybe<TValue> WrapSuccess(TValue value) => Maybe.From(value);

    /// <inheritdoc />
    protected override Maybe<TValue> OnValidationFailure() => default;

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Maybe<TValue> value, JsonSerializerOptions options)
    {
        if (value.HasNoValue)
        {
            writer.WriteNullValue();
            return;
        }

        PrimitiveJsonWriter.Write(writer, value.Value.Value);
    }
}