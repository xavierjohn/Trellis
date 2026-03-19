namespace Trellis;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// A JSON converter for value objects that implement <see cref="IParsable{TSelf}"/>.
/// Enables automatic serialization and deserialization of parsable value objects in ASP.NET Core APIs and System.Text.Json scenarios.
/// </summary>
/// <typeparam name="T">The type of the value object to convert. Must implement <see cref="IParsable{TSelf}"/>.</typeparam>
/// <remarks>
/// <para>
/// This converter provides seamless JSON integration for value objects by:
/// <list type="bullet">
/// <item>Serializing value objects to their string representation</item>
/// <item>Deserializing JSON strings back to value objects using the Parse method</item>
/// <item>Maintaining type safety throughout the serialization process</item>
/// <item>Working with ASP.NET Core model binding and validation</item>
/// </list>
/// </para>
/// <para>
/// The converter delegates to the value object's <see cref="IParsable{TSelf}.Parse"/> and
/// <see cref="object.ToString"/> methods, ensuring consistent behavior between JSON
/// serialization and other parsing scenarios.
/// </para>
/// <para>
/// Common usage:
/// <list type="bullet">
/// <item>Applied via <c>[JsonConverter]</c> attribute on value object classes</item>
/// <item>Enables clean JSON APIs without manual conversion code</item>
/// <item>Supports both request deserialization and response serialization</item>
/// <item>Works with minimal APIs, MVC controllers, and HttpClient</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Applying the converter to a value object:
/// <code>
/// [JsonConverter(typeof(ParsableJsonConverter&lt;EmailAddress&gt;))]
/// public partial class EmailAddress : ScalarValueObject&lt;string&gt;, IParsable&lt;EmailAddress&gt;
/// {
///     private EmailAddress(string value) : base(value) { }
///     
///     public static EmailAddress Parse(string s, IFormatProvider? provider)
///     {
///         var result = TryCreate(s);
///         if (result.IsFailure)
///             throw new FormatException("Invalid email address");
///         return result.Value;
///     }
///     
///     public static bool TryParse(string? s, IFormatProvider? provider, 
///         out EmailAddress result)
///     {
///         var r = TryCreate(s);
///         result = r.IsSuccess ? r.Value : default!;
///         return r.IsSuccess;
///     }
///     
///     public override string ToString() => Value;
/// }
/// </code>
/// </example>
/// <example>
/// Automatic JSON serialization in DTOs:
/// <code>
/// public record UserDto(
///     UserId Id,
///     EmailAddress Email,
///     FirstName FirstName
/// );
/// 
/// var user = new UserDto(
///     UserId.NewUnique(),
///     EmailAddress.Create("user@example.com"),
///     FirstName.Create("John")
/// );
/// 
/// var json = JsonSerializer.Serialize(user);
/// // Produces:
/// // {
/// //   "id": "550e8400-e29b-41d4-a716-446655440000",
/// //   "email": "user@example.com",
/// //   "firstName": "John"
/// // }
/// 
/// var deserialized = JsonSerializer.Deserialize&lt;UserDto&gt;(json);
/// // Automatically parses strings back to value objects
/// </code>
/// </example>
/// <example>
/// Using in API endpoints:
/// <code>
/// public record CreateUserRequest(
///     EmailAddress Email,
///     FirstName FirstName,
///     LastName LastName
/// );
/// 
/// // Minimal API
/// app.MapPost("/users", (CreateUserRequest request) =>
/// {
///     // Value objects are already parsed and validated from JSON
///     return _userService.CreateUser(request.Email, request.FirstName, request.LastName)
///         .ToHttpResult();
/// });
/// 
/// // MVC Controller
/// [HttpPost]
/// public ActionResult&lt;UserDto&gt; CreateUser(CreateUserRequest request)
/// {
///     // ASP.NET Core automatically deserializes JSON strings to value objects
///     return _userService.CreateUser(request.Email, request.FirstName, request.LastName)
///         .ToActionResult(this);
/// }
/// 
/// // JSON Request:
/// // {
/// //   "email": "user@example.com",
/// //   "firstName": "John",
/// //   "lastName": "Doe"
/// // }
/// </code>
/// </example>
/// <example>
/// Error handling during deserialization:
/// <code>
/// // Invalid JSON value throws JsonException during deserialization
/// var invalidJson = @"{""email"": ""not-a-valid-email""}";
/// 
/// try
/// {
///     var dto = JsonSerializer.Deserialize&lt;UserDto&gt;(invalidJson);
/// }
/// catch (JsonException ex)
/// {
///     // Exception message contains the FormatException from EmailAddress.Parse
///     Console.WriteLine($"JSON deserialization failed: {ex.Message}");
/// }
/// 
/// // In ASP.NET Core, this automatically becomes a 400 Bad Request
/// // with model validation error details
/// </code>
/// </example>
/// <seealso cref="IParsable{TSelf}"/>
/// <seealso cref="JsonConverter{T}"/>
public class ParsableJsonConverter<T> :
    JsonConverter<T> where T : IParsable<T>
{
    /// <summary>
    /// Reads and converts the JSON to type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="reader">The <see cref="Utf8JsonReader"/> to read from.</param>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <param name="options">The serializer options to use.</param>
    /// <returns>
    /// The converted value object of type <typeparamref name="T"/>.
    /// </returns>
    /// <exception cref="JsonException">
    /// Thrown when the JSON value cannot be parsed into a valid value object.
    /// The inner exception contains the <see cref="FormatException"/> from the value object's Parse method.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method:
    /// <list type="bullet">
    /// <item>Reads the JSON value from string, number, boolean, or null tokens</item>
    /// <item>Delegates to <typeparamref name="T"/>'s <see cref="IParsable{TSelf}.Parse"/> method</item>
    /// <item>Throws <see cref="JsonException"/> if parsing fails</item>
    /// </list>
    /// </para>
    /// <para>
    /// In ASP.NET Core, deserialization failures are automatically handled and converted
    /// to 400 Bad Request responses with appropriate error messages.
    /// </para>
    /// </remarks>
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt64(out var l) => l.ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Number when reader.TryGetDecimal(out var d) => d.ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Number => reader.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected JSON token type '{reader.TokenType}' when deserializing '{typeof(T).Name}'. Expected string, number, boolean, or null.")
        };

        if (raw is null)
            throw new JsonException($"Cannot deserialize null JSON value to non-nullable type '{typeof(T).Name}'.");

        return T.Parse(raw, default);
    }

    private static readonly bool s_isNumericType = IsNumericScalarType();

    private static bool IsNumericScalarType()
    {
        // Walk the type hierarchy to find ScalarValueObject<TSelf, T> and check if T is numeric
        var type = typeof(T);
        while (type is not null)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition().Name.StartsWith("ScalarValueObject", StringComparison.Ordinal))
            {
                var primitiveType = type.GetGenericArguments()[1]; // T in ScalarValueObject<TSelf, T>
                return primitiveType == typeof(int) || primitiveType == typeof(long)
                    || primitiveType == typeof(decimal) || primitiveType == typeof(double)
                    || primitiveType == typeof(float) || primitiveType == typeof(short)
                    || primitiveType == typeof(byte);
            }

            type = type.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Writes a specified value as JSON.
    /// </summary>
    /// <param name="writer">The <see cref="Utf8JsonWriter"/> to write to.</param>
    /// <param name="value">The value object to convert to JSON.</param>
    /// <param name="options">The serializer options to use.</param>
    /// <remarks>
    /// <para>
    /// For value objects backed by numeric primitives (int, decimal, etc.), writes a JSON number.
    /// For all others, writes a JSON string via <see cref="object.ToString"/>.
    /// </para>
    /// </remarks>
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var stringValue = value.ToString();

        if (s_isNumericType
            && stringValue is not null
            && decimal.TryParse(stringValue, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var numericValue))
        {
            writer.WriteNumberValue(numericValue);
        }
        else
        {
            writer.WriteStringValue(stringValue);
        }
    }
}