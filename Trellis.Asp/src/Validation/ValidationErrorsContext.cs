namespace Trellis.Asp;

using System.Collections.Immutable;
using System.Text;
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
    private static readonly AsyncLocal<ImmutableList<string>?> s_ancestorPath = new();

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
    /// In reflection mode, the property name is set by <c>PropertyNameAwareConverter&lt;T&gt;</c>
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
    /// Nested scopes are supported; each scope maintains its own error collection and starts at the
    /// document root, so the ambient current-property name and ancestor path are reset for the new
    /// scope and restored when it is disposed.
    /// </remarks>
    public static IDisposable BeginScope()
    {
        var previous = s_current.Value;
        var previousPropertyName = s_currentPropertyName.Value;
        var previousAncestorPath = s_ancestorPath.Value;
        s_current.Value = new ErrorCollector();
        s_currentPropertyName.Value = null;
        s_ancestorPath.Value = null;
        return new Scope(previous, previousPropertyName, previousAncestorPath);
    }

    /// <summary>
    /// Pushes a path segment (a container property name or a collection index) onto the current
    /// ancestor path; disposing the returned scope pops it. Container and collection converters use
    /// this so a value object nested inside a collection or another object reports an index-precise
    /// field path (e.g. <c>/members/0/email</c>) rather than just the leaf property name.
    /// </summary>
    /// <param name="segment">The unescaped path segment to push.</param>
    /// <returns>An <see cref="IDisposable"/> that pops the segment when disposed.</returns>
    internal static IDisposable PushPathSegment(string segment)
    {
        var previous = s_ancestorPath.Value ?? ImmutableList<string>.Empty;
        s_ancestorPath.Value = previous.Add(segment);
        return new PathSegmentScope(previous);
    }

    // Builds the RFC 6901 JSON Pointer for a field, prefixed with the current ancestor path. Mirrors
    // InputPointer.ForProperty so the public AddError(string, ...) contract is preserved: a value that
    // already starts with '/' is treated as a fully-formed pointer (its segments are not re-escaped),
    // and an empty value targets the ancestor (the document root when there is no ancestor).
    private static string BuildPointer(string fieldName)
    {
        var ancestor = AncestorPointer();
        if (string.IsNullOrEmpty(fieldName))
            return ancestor;
        if (fieldName[0] == '/')
            return ancestor + fieldName;
        return ancestor + "/" + EscapeSegment(fieldName);
    }

    private static string AncestorPointer()
    {
        var path = s_ancestorPath.Value;
        if (path is null || path.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var segment in path)
            sb.Append('/').Append(EscapeSegment(segment));

        return sb.ToString();
    }

    // RFC 6901 §3: '~' is escaped first (to '~0'), then '/' (to '~1').
    private static string EscapeSegment(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

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
        s_current.Value?.AddFieldViolation(
            new FieldViolation(new InputPointer(BuildPointer(fieldName)), "validation.error") { Detail = errorMessage });

    /// <summary>
    /// Adds all field violations and rule violations from an existing <see cref="Error.InvalidInput"/> to the current scope.
    /// </summary>
    /// <param name="unprocessableContent">The error whose violations should be merged.</param>
    /// <remarks>
    /// <para>If no scope is active, this method is a no-op.</para>
    /// <para>
    /// The merge preserves each field violation's full structure — including its
    /// <see cref="FieldViolation.ReasonCode"/>, <see cref="FieldViolation.Args"/>,
    /// and <see cref="FieldViolation.Detail"/> — and also preserves any top-level
    /// <see cref="Error.InvalidInput.Rules"/> entries. Used by the framework's
    /// reflection-mode converters and by AOT-generated scalar value converters to surface
    /// rich, structured validation failures.
    /// </para>
    /// </remarks>
    public static void AddError(Error.InvalidInput unprocessableContent)
    {
        var collector = s_current.Value;
        if (collector is null)
            return;

        var ancestor = AncestorPointer();
        foreach (var fieldViolation in unprocessableContent.Fields)
        {
            var prefixed = ancestor.Length == 0
                ? fieldViolation
                : fieldViolation with { Field = new InputPointer(ancestor + fieldViolation.Field.Path) };
            collector.AddFieldViolation(prefixed);
        }

        foreach (var ruleViolation in unprocessableContent.Rules)
        {
            var prefixedRule = ancestor.Length == 0 || ruleViolation.Fields.IsEmpty
                ? ruleViolation
                : ruleViolation with
                {
                    Fields = ruleViolation.Fields.Items
                        .Select(pointer => new InputPointer(ancestor + pointer.Path))
                        .ToImmutableArray(),
                };
            collector.AddRuleViolation(prefixedRule);
        }
    }

    /// <summary>
    /// Gets whether an error has already been collected for the given leaf field at the current
    /// ancestor path. Used to avoid double-reporting a property as both invalid and "required".
    /// </summary>
    /// <param name="fieldName">The leaf field name to check.</param>
    internal static bool HasErrorForField(string fieldName) =>
        s_current.Value?.HasErrorForPath(BuildPointer(fieldName)) ?? false;

    /// <summary>
    /// Gets the aggregated <see cref="Error.InvalidInput"/> from the current scope, or null if no errors were collected.
    /// </summary>
    /// <returns>
    /// An <see cref="Error.InvalidInput"/> containing all collected field and rule violations,
    /// or <c>null</c> if no validation errors were recorded.
    /// </returns>
    public static Error.InvalidInput? GetUnprocessableContent() =>
        s_current.Value?.GetUnprocessableContent();

    /// <summary>
    /// Gets whether any validation errors have been collected in the current scope.
    /// </summary>
    public static bool HasErrors => s_current.Value?.HasErrors ?? false;

    private sealed class Scope : IDisposable
    {
        private readonly ErrorCollector? _previous;
        private readonly string? _previousPropertyName;
        private readonly ImmutableList<string>? _previousAncestorPath;

        public Scope(ErrorCollector? previous, string? previousPropertyName, ImmutableList<string>? previousAncestorPath)
        {
            _previous = previous;
            _previousPropertyName = previousPropertyName;
            _previousAncestorPath = previousAncestorPath;
        }

        public void Dispose()
        {
            s_current.Value = _previous;
            s_currentPropertyName.Value = _previousPropertyName;
            s_ancestorPath.Value = _previousAncestorPath;
        }
    }

    private sealed class PathSegmentScope : IDisposable
    {
        private readonly ImmutableList<string> _previous;

        public PathSegmentScope(ImmutableList<string> previous) => _previous = previous;

        public void Dispose() => s_ancestorPath.Value = _previous;
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

        public bool HasErrorForPath(string path)
        {
            lock (_lock)
            {
                return _fieldErrors.ContainsKey(path);
            }
        }

        public void AddFieldViolation(FieldViolation violation)
        {
            lock (_lock)
            {
                var key = violation.Field.Path;
                if (!_fieldErrors.TryGetValue(key, out var errors))
                {
                    errors = [];
                    _fieldErrors[key] = errors;
                }

                if (!errors.Contains(violation))
                {
                    errors.Add(violation);
                }
            }
        }

        public void AddRuleViolation(RuleViolation violation)
        {
            lock (_lock)
            {
                if (!_ruleErrors.Contains(violation))
                {
                    _ruleErrors.Add(violation);
                }
            }
        }

        public Error.InvalidInput? GetUnprocessableContent()
        {
            lock (_lock)
            {
                if (_fieldErrors.Count == 0 && _ruleErrors.Count == 0)
                    return null;

                var fieldArray = _fieldErrors
                    .SelectMany(kvp => kvp.Value)
                    .ToArray();

                var ruleArray = _ruleErrors.ToArray();

                return new Error.InvalidInput(
                    EquatableArray.Create(fieldArray),
                    EquatableArray.Create(ruleArray))
                {
                    Detail = "One or more validation errors occurred.",
                };
            }
        }
    }
}