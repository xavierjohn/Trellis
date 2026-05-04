namespace Trellis.Asp;

using Trellis;

/// <summary>
/// Provides a context for collecting validation errors during JSON deserialization.
/// Uses AsyncLocal to maintain thread-safe, request-scoped error collection.
/// </summary>
/// <remarks>
/// <para>
/// This class enables the pattern of collecting all validation errors from value objects
/// during JSON deserialization, rather than failing on the first error. This allows
/// returning a comprehensive list of validation failures to the client.
/// </para>
/// <para>
/// The context is automatically scoped per async operation, making it safe for use
/// in concurrent web request scenarios.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using (ValidationErrorsContext.BeginScope())
/// {
///     // Deserialize JSON - errors are collected
///     var dto = JsonSerializer.Deserialize&lt;CreateUserDto&gt;(json, options);
///     
///     // Check for collected errors
///     var error = ValidationErrorsContext.GetUnprocessableContent();
///     if (error is not null)
///     {
///         return Results.Problem(detail: error.Detail, statusCode: 422);
///     }
/// }
/// </code>
/// </example>
public static class ValidationErrorsContext
{
    private static readonly AsyncLocal<ErrorCollector?> s_current = new();
    private static readonly AsyncLocal<string?> s_currentPropertyName = new();

    /// <summary>
    /// Gets the current error collector for the async context, or null if no scope is active.
    /// </summary>
    internal static ErrorCollector? Current => s_current.Value;

    /// <summary>
    /// Gets or sets the current property name being deserialized.
    /// Used by ValidatingJsonConverter (reflection mode) and AOT-generated converters
    /// to determine the field name for validation errors.
    /// </summary>
    /// <remarks>
    /// In reflection mode, the property name is set by <c>PropertyNameAwareConverter&lt;TInner, TValue&gt;</c>
    /// during JSON deserialization. In AOT mode, generated converters read this property and fall back
    /// to a camel-cased type name when no scope is active. Using AsyncLocal ensures thread-safety
    /// and proper isolation across concurrent requests.
    /// </remarks>
    public static string? CurrentPropertyName
    {
        get => s_currentPropertyName.Value;
        set => s_currentPropertyName.Value = value;
    }

    /// <summary>
    /// Begins a new validation error collection scope.
    /// </summary>
    /// <returns>An <see cref="IDisposable"/> that ends the scope when disposed.</returns>
    /// <remarks>
    /// Always use this in a using statement or block to ensure proper cleanup.
    /// Nested scopes are supported; each scope maintains its own error collection.
    /// </remarks>
    public static IDisposable BeginScope()
    {
        var previous = s_current.Value;
        var previousPropertyName = s_currentPropertyName.Value;
        s_current.Value = new ErrorCollector();
        return new Scope(previous, previousPropertyName);
    }

    /// <summary>
    /// Adds a validation error for a specific field to the current scope.
    /// </summary>
    /// <param name="fieldName">The name of the field that failed validation.</param>
    /// <param name="errorMessage">The validation error message.</param>
    /// <remarks>
    /// If no scope is active, this method is a no-op. Called by the framework's reflection-mode
    /// converters and by AOT-generated scalar value converters to surface deserialization
    /// failures as 422 responses via <see cref="ScalarValueValidationMiddleware"/>.
    /// </remarks>
    public static void AddError(string fieldName, string errorMessage) =>
        s_current.Value?.AddError(fieldName, errorMessage);

    /// <summary>
    /// Adds all field violations and rule violations from an existing <see cref="Error.UnprocessableContent"/> to the current scope.
    /// </summary>
    /// <param name="unprocessableContent">The error whose violations should be merged.</param>
    /// <remarks>
    /// <para>If no scope is active, this method is a no-op.</para>
    /// <para>
    /// The merge preserves each field violation's full structure — including its
    /// <see cref="FieldViolation.ReasonCode"/>, <see cref="FieldViolation.Args"/>,
    /// and <see cref="FieldViolation.Detail"/> — and also preserves any top-level
    /// <see cref="Error.UnprocessableContent.Rules"/> entries. Used by the framework's
    /// reflection-mode converters and by AOT-generated scalar value converters to surface
    /// rich, structured validation failures.
    /// </para>
    /// </remarks>
    public static void AddError(Error.UnprocessableContent unprocessableContent) =>
        s_current.Value?.AddError(unprocessableContent);

    /// <summary>
    /// Gets the aggregated <see cref="Error.UnprocessableContent"/> from the current scope, or null if no errors were collected.
    /// </summary>
    /// <returns>
    /// An <see cref="Error.UnprocessableContent"/> containing all collected field and rule violations,
    /// or <c>null</c> if no validation errors were recorded.
    /// </returns>
    public static Error.UnprocessableContent? GetUnprocessableContent() =>
        s_current.Value?.GetUnprocessableContent();

    /// <summary>
    /// Gets whether any validation errors have been collected in the current scope.
    /// </summary>
    public static bool HasErrors => s_current.Value?.HasErrors ?? false;

    private sealed class Scope : IDisposable
    {
        private readonly ErrorCollector? _previous;
        private readonly string? _previousPropertyName;

        public Scope(ErrorCollector? previous, string? previousPropertyName)
        {
            _previous = previous;
            _previousPropertyName = previousPropertyName;
        }

        public void Dispose()
        {
            s_current.Value = _previous;
            s_currentPropertyName.Value = _previousPropertyName;
        }
    }

    internal sealed class ErrorCollector
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, List<FieldViolation>> _fieldErrors = new(StringComparer.Ordinal);
        private readonly List<RuleViolation> _ruleErrors = [];

        public bool HasErrors
        {
            get
            {
                lock (_lock)
                {
                    return _fieldErrors.Count > 0 || _ruleErrors.Count > 0;
                }
            }
        }

        public bool HasErrorForField(string fieldName)
        {
            lock (_lock)
            {
                return _fieldErrors.ContainsKey(fieldName);
            }
        }

        public void AddError(string fieldName, string errorMessage)
        {
            lock (_lock)
            {
                if (!_fieldErrors.TryGetValue(fieldName, out var errors))
                {
                    errors = [];
                    _fieldErrors[fieldName] = errors;
                }

                var violation = new FieldViolation(InputPointer.ForProperty(fieldName), "validation.error") { Detail = errorMessage };
                if (!ContainsByDetail(errors, errorMessage))
                {
                    errors.Add(violation);
                }
            }
        }

        public void AddError(Error.UnprocessableContent unprocessableContent)
        {
            lock (_lock)
            {
                foreach (var fieldViolation in unprocessableContent.Fields)
                {
                    var fieldName = fieldViolation.Field.Path.TrimStart('/');
                    if (!_fieldErrors.TryGetValue(fieldName, out var errors))
                    {
                        errors = [];
                        _fieldErrors[fieldName] = errors;
                    }

                    if (!errors.Contains(fieldViolation))
                    {
                        errors.Add(fieldViolation);
                    }
                }

                foreach (var ruleViolation in unprocessableContent.Rules)
                {
                    if (!_ruleErrors.Contains(ruleViolation))
                    {
                        _ruleErrors.Add(ruleViolation);
                    }
                }
            }
        }

        public Error.UnprocessableContent? GetUnprocessableContent()
        {
            lock (_lock)
            {
                if (_fieldErrors.Count == 0 && _ruleErrors.Count == 0)
                    return null;

                var fieldArray = _fieldErrors
                    .SelectMany(kvp => kvp.Value)
                    .ToArray();

                var ruleArray = _ruleErrors.ToArray();

                return new Error.UnprocessableContent(
                    EquatableArray.Create(fieldArray),
                    EquatableArray.Create(ruleArray))
                {
                    Detail = "One or more validation errors occurred.",
                };
            }
        }

        private static bool ContainsByDetail(List<FieldViolation> existing, string detail)
        {
            foreach (var v in existing)
            {
                if (string.Equals(v.Detail, detail, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}