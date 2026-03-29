namespace Trellis;

/// <summary>
/// Interface for scalar value objects that can be created with validation.
/// Enables automatic ASP.NET Core model binding and JSON serialization.
/// </summary>
/// <typeparam name="TSelf">The value object type itself (CRTP pattern)</typeparam>
/// <typeparam name="TPrimitive">The underlying primitive type (must be IComparable)</typeparam>
/// <remarks>
/// <para>
/// This interface uses the Curiously Recurring Template Pattern (CRTP) to enable
/// static abstract methods on the value object type. This allows model binders and
/// JSON converters to call <see cref="TryCreate(TPrimitive, string?)"/> without reflection.
/// </para>
/// <para>
/// When a type implements this interface, it can be automatically validated during:
/// <list type="bullet">
/// <item>ASP.NET Core model binding (from route, query, form, or header values)</item>
/// <item>JSON deserialization (from request body)</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Implementing in a custom value object:
/// <code><![CDATA[
/// public class EmailAddress : ScalarValueObject<EmailAddress, string>, IScalarValueObject<EmailAddress, string>
/// {
///     private EmailAddress(string value) : base(value) { }
///
///     public static Result<EmailAddress> TryCreate(string value, string? fieldName = null) =>
///         value.ToResult(Error.Validation("Email is required", fieldName ?? "email"))
///             .Ensure(e => e.Contains("@"), Error.Validation("Invalid email", fieldName ?? "email"))
///             .Map(e => new EmailAddress(e));
/// }
/// ]]></code>
/// </example>
public interface IScalarValue<TSelf, TPrimitive>
    where TSelf : IScalarValue<TSelf, TPrimitive>
    where TPrimitive : IComparable
{
    /// <summary>
    /// Attempts to create a validated scalar value from a primitive value.
    /// </summary>
    /// <param name="value">The raw primitive value</param>
    /// <param name="fieldName">
    /// Optional field name for validation error messages. If null, implementations should use
    /// a default field name based on the type name (e.g., "emailAddress" for EmailAddress type).
    /// </param>
    /// <returns>Success with the scalar value, or Failure with validation errors</returns>
    /// <remarks>
    /// <para>
    /// This method is called by model binders and JSON converters to create scalar values
    /// with validation. The validation errors are collected and returned through the
    /// standard ASP.NET Core validation infrastructure.
    /// </para>
    /// <para>
    /// When called from ASP.NET Core model binding or JSON deserialization, the fieldName
    /// parameter is automatically populated with the property name from the DTO.
    /// </para>
    /// </remarks>
    static abstract Result<TSelf> TryCreate(TPrimitive value, string? fieldName = null);

    /// <summary>
    /// Attempts to create a validated scalar value from a string representation.
    /// </summary>
    /// <param name="value">The raw string value to parse and validate</param>
    /// <param name="fieldName">
    /// Optional field name for validation error messages. If null, implementations should use
    /// a default field name based on the type name.
    /// </param>
    /// <returns>Success with the scalar value, or Failure with validation errors</returns>
    static abstract Result<TSelf> TryCreate(string? value, string? fieldName = null);

    /// <summary>
    /// Creates a validated scalar value from a primitive value.
    /// Throws an exception if validation fails.
    /// </summary>
    /// <param name="value">The raw primitive value</param>
    /// <returns>The validated scalar value</returns>
    /// <exception cref="InvalidOperationException">Thrown when validation fails</exception>
    /// <remarks>
    /// <para>
    /// Use this method when you know the value is valid (e.g., in tests, with constants,
    /// or when building from other validated values). This provides cleaner code
    /// than calling <c>Create(...)</c> through a manual <c>TryCreate(...).Value</c> pattern.
    /// </para>
    /// <para>
    /// ⚠️ Don't use this method with user input or uncertain data - use <see cref="TryCreate(TPrimitive, string?)"/>
    /// instead to handle validation errors gracefully.
    /// </para>
    /// <para>
    /// The default implementation calls <see cref="TryCreate(TPrimitive, string?)"/> and throws if validation fails.
    /// You can override this if you need custom error handling behavior.
    /// </para>
    /// </remarks>
    /// <example>
    /// Use in tests or with known-valid values:
    /// <code><![CDATA[
    /// // ✅ Good - Test data
    /// var email = EmailAddress.Create("test@example.com");
    /// 
    /// // ✅ Good - Building from validated data
    /// var total = Money.Create(item1.Amount + item2.Amount, "USD");
    /// 
    /// // ❌ Bad - User input (use TryCreate instead)
    /// var email = EmailAddress.Create(userInput); 
    /// ]]></code>
    /// </example>
    static virtual TSelf Create(TPrimitive value)
    {
        var result = TSelf.TryCreate(value);
        if (result.IsFailure)
            throw new InvalidOperationException($"Failed to create {typeof(TSelf).Name}: {result.Error.Detail}");

        return result.Value;
    }

    /// <summary>
    /// Gets the underlying primitive value for serialization.
    /// </summary>
    /// <value>The primitive value wrapped by this scalar value.</value>
    TPrimitive Value { get; }
}