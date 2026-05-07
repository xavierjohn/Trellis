namespace Trellis.Asp;

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Trellis;
using Trellis.Asp.Validation;

/// <summary>
/// Middleware that creates a validation error collection scope for each request.
/// This enables ValidatingJsonConverter to collect validation errors
/// across the entire request deserialization process.
/// </summary>
/// <remarks>
/// <para>
/// This middleware should be registered early in the pipeline, before any middleware
/// that might deserialize JSON request bodies.
/// </para>
/// <para>
/// For each request:
/// <list type="bullet">
/// <item>Creates a new validation error collection scope</item>
/// <item>Allows the request to proceed through the pipeline</item>
/// <item>Catches <see cref="BadHttpRequestException"/> for <see cref="IScalarValue{TSelf, TPrimitive}"/> parameter binding failures and returns validation problem</item>
/// <item>Cleans up the scope when the request completes</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Registering the middleware in Program.cs:
/// <code>
/// app.UseScalarValueValidation();
/// // ... other middleware
/// app.MapControllers();
/// </code>
/// </example>
public sealed class ScalarValueValidationMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Creates a new instance of <see cref="ScalarValueValidationMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public ScalarValueValidationMiddleware(RequestDelegate next) =>
        _next = next;

    /// <summary>
    /// Invokes the middleware, wrapping the request in a validation scope.
    /// </summary>
    /// <param name="context">The HTTP context for the request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        using (ValidationErrorsContext.BeginScope())
        {
            try
            {
                await _next(context).ConfigureAwait(false);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status400BadRequest)
            {
                if (ex.InnerException is JsonException)
                {
                    // Handle JSON body deserialization failures (e.g., missing required properties)
                    await WriteJsonDeserializationErrorAsync(context, ex).ConfigureAwait(false);
                }
                else if (TryCreateScalarBindingErrors(context, out var errors))
                {
                    await WriteValidationProblemAsync(context, errors).ConfigureAwait(false);
                }
                else if (HasEndpointParameterMetadata(context))
                {
                    // Endpoint binding failed, but Trellis could not map it to a scalar-value parameter.
                    // Let ASP.NET Core's normal BadHttpRequestException handling deal with non-Trellis parameters.
                    throw;
                }
                else
                {
                    // Unrecognized 400 format - return generic 400 to prevent 500 propagation
                    await WriteGenericBadRequestAsync(context).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task WriteGenericBadRequestAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        // Use an empty string key so this fallback shares the same MVC dot+bracket wire shape
        // as every other Trellis.Asp ValidationProblem emitter (matches JsonPointerToMvc.Translate("")).
        var errors = new Dictionary<string, string[]>
        {
            [string.Empty] = ["The request was invalid."]
        };

        var result = Results.ValidationProblem(errors);
        await result.ExecuteAsync(context).ConfigureAwait(false);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072:Target parameter argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method. The return value of the source method does not have matching annotations.",
        Justification = "The type check for IScalarValue interfaces is safe - we only check interface implementation, not instantiate or invoke members.")]
    [UnconditionalSuppressMessage("Trimming", "IL2073:Return type does not satisfy 'DynamicallyAccessedMembersAttribute' requirements.",
        Justification = "The returned type comes from ParameterInfo.ParameterType which preserves type metadata at runtime.")]
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)]
    private static Type? GetScalarValueParameterType(IParameterBindingMetadata parameterMetadata)
    {
        // Check if the parameter type implements IScalarValue<,>
        var parameterType = parameterMetadata.ParameterInfo.ParameterType;

        // Handle nullable types (e.g., OrderState?)
        var underlyingType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;

        if (ScalarValueTypeHelper.IsScalarValue(underlyingType))
            return underlyingType;

        var maybeInnerType = ScalarValueTypeHelper.GetMaybeInnerType(underlyingType);
        return maybeInnerType is not null && ScalarValueTypeHelper.IsScalarValue(maybeInnerType)
            ? maybeInnerType
            : null;
    }

    private static bool TryCreateScalarBindingErrors(
        HttpContext context,
        out IDictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var parameterMetadata in GetEndpointParameterMetadata(context))
        {
            var parameterName = parameterMetadata.Name;
            if (string.IsNullOrWhiteSpace(parameterName))
                continue;

            var scalarValueType = GetScalarValueParameterType(parameterMetadata);
            if (scalarValueType is null)
                continue;

            var rawValue = GetRawParameterValue(context, parameterName);
            if (rawValue is null)
                continue;

            var parameterErrors = ScalarValueTypeHelper.GetValidationErrors(scalarValueType, rawValue, parameterName);
            if (parameterErrors is null)
                continue;

            foreach (var (fieldName, details) in parameterErrors)
                errors[fieldName] = details;
        }

        return errors.Count > 0;
    }

    private static string? GetRawParameterValue(HttpContext context, string parameterName)
    {
        if (context.Request.RouteValues.TryGetValue(parameterName, out var routeValue))
            return routeValue?.ToString();

        if (context.Request.Query.TryGetValue(parameterName, out var queryValue))
            return queryValue.ToString();

        return null;
    }

    private static bool HasEndpointParameterMetadata(HttpContext context) =>
        GetEndpointParameterMetadata(context).Any();

    private static IEnumerable<IParameterBindingMetadata> GetEndpointParameterMetadata(HttpContext context) =>
        context.GetEndpoint()?.Metadata.OfType<IParameterBindingMetadata>() ?? [];

    private static async Task WriteValidationProblemAsync(
        HttpContext context,
        IDictionary<string, string[]> errors)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        // Use Results.ValidationProblem for consistent response format
        var result = Results.ValidationProblem(errors);
        await result.ExecuteAsync(context).ConfigureAwait(false);
    }

    private static async Task WriteJsonDeserializationErrorAsync(HttpContext context, BadHttpRequestException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        // System.Text.Json populates JsonException.Path automatically as the deserializer
        // unwinds (e.g. "$.items[0].amount"). Pull it from any JsonException — both the
        // built-in failures and the TrellisJsonValidationException subclass — so the wire-key
        // shape is consistent regardless of which converter raised the error.
        var rawPath = (ex.InnerException as JsonException)?.Path;

        // Translate JSON Path notation to MVC dot+bracket convention so this 400 path shares the
        // same wire-key shape as every other Trellis.Asp ValidationProblem emitter.
        var key = JsonPathToMvcKey(rawPath);

        // Structured shape: when the inner exception is a TrellisJsonValidationException
        // carrying a structured Error.UnprocessableContent with at least one FieldViolation,
        // emit ONE wire entry per FieldViolation. The parent path (`key`) becomes the prefix;
        // each violation's leaf path is appended in MVC dot+bracket convention via
        // JsonPointerToMvc.Translate.
        //
        // Rules-only UnprocessableContent (no FieldViolations) falls through to the unstructured
        // branch so the curated exception message surfaces under the parent key — emitting an
        // empty `errors` object would silently swallow the rule violation.
        if (ex.InnerException is TrellisJsonValidationException { UnprocessableContent: { Fields.Length: > 0 } structuredError })
        {
            var perLeafErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var fv in structuredError.Fields)
            {
                var leafKey = JsonPointerToMvc.Translate(fv.Field.Path);
                var combinedKey = CombineMvcKeys(key, leafKey);

                var detail = !string.IsNullOrEmpty(fv.Detail) ? fv.Detail : fv.ReasonCode;
                if (perLeafErrors.TryGetValue(combinedKey, out var existing))
                {
                    var merged = new string[existing.Length + 1];
                    Array.Copy(existing, merged, existing.Length);
                    merged[^1] = detail;
                    perLeafErrors[combinedKey] = merged;
                }
                else
                {
                    perLeafErrors[combinedKey] = [detail];
                }
            }

            var structuredResult = Results.ValidationProblem(perLeafErrors);
            await structuredResult.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        // Unstructured shape: used when the inner exception has no structured payload
        // (plain JsonException from System.Text.Json's built-in failures, or a Trellis
        // converter throw without an associated Error.UnprocessableContent — e.g., missing
        // required property, unsupported primitive type). Surface the curated message for
        // TrellisJsonValidationException; emit a generic message for plain JsonException
        // because System.Text.Json's built-in errors can include internal type names.
        var message = ex.InnerException is Trellis.TrellisJsonValidationException tjx
            ? tjx.Message
            : "The request body contains invalid JSON.";

        var errors = new Dictionary<string, string[]>
        {
            [key] = [message],
        };

        var result = Results.ValidationProblem(errors);
        await result.ExecuteAsync(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Translates System.Text.Json's <see cref="JsonException.Path"/> (JSONPath syntax) to
    /// the MVC dot+bracket key shape used by every other Trellis.Asp <c>ValidationProblem</c>
    /// emitter (see <see cref="JsonPointerToMvc"/>). This keeps the wire shape consistent
    /// regardless of which layer produced the 400.
    /// </summary>
    /// <remarks>
    /// <para>
    /// STJ uses JSONPath: <c>$</c> for root, <c>$.foo.bar</c> for dot-property segments,
    /// <c>$[0]</c> for array indices, and <c>$['weird name']</c> bracket-quoted notation
    /// for property names containing characters that are not valid JSONPath identifiers
    /// (space, dot, slash, bracket, single quote, etc.). STJ does <b>not</b> escape
    /// embedded single quotes in bracket-quoted segments — for the dictionary key
    /// <c>a'b</c> it emits the literal string <c>$['a'b']</c>. The forward-scan-with-
    /// lookahead heuristic below closes a bracket-quoted segment only at <c>'</c>
    /// followed by <c>]</c> followed by <c>.</c>, <c>[</c>, or end-of-string, which
    /// recovers the property name correctly for every observed STJ output (including
    /// nested cases like <c>$['a'b'].foo</c>, <c>$['a'b'][0]</c>, <c>$['a'.b']</c>).
    /// </para>
    /// <para>
    /// Mapping: drop the leading <c>$</c>; emit each subsequent segment as MVC produces it,
    /// using bare property names joined with <c>.</c> and numeric indices as <c>[N]</c>.
    /// Bracket-quoted property segments are unquoted and emitted as bare property segments.
    /// Empty property names (<c>$.</c>, <c>$..foo</c>, <c>$.foo.</c>, <c>$['']</c>) emit
    /// <c>[""]</c> to match <see cref="JsonPointerToMvc.Translate"/> output for the
    /// equivalent JSON Pointer (<c>/</c> → <c>[""]</c>).
    /// </para>
    /// <para>
    /// <b>Known limitation:</b> STJ's path serialization is genuinely lossy for property
    /// names containing the literal sequence <c>'][</c> (e.g. dictionary keys
    /// <c>a'][</c>, <c>a'][b</c>, <c>a'.b']['foo</c>). For these adversarial inputs the
    /// heuristic prefers the "multiple segments" interpretation over the
    /// "embedded <c>'][</c> in a single property name" interpretation, so the resulting
    /// MVC key may not equal <see cref="JsonPointerToMvc.Translate"/>'s output for the
    /// equivalent JSON Pointer. This is a deliberate trade-off: legitimate paths with
    /// adjacent non-identifier property names (e.g. <c>$['weird name']['another weird']</c>)
    /// are common; property names containing the literal <c>'][</c> sequence are not.
    /// Consumers requiring lossless field paths for adversarial keys should rely on
    /// <c>RuleViolation</c> payloads carrying raw JSON Pointers in
    /// <c>extensions["rules"][n].fields[]</c>.
    /// </para>
    /// </remarks>
    private static string JsonPathToMvcKey(string? jsonExceptionPath)
    {
        if (string.IsNullOrEmpty(jsonExceptionPath) || jsonExceptionPath == "$")
            return string.Empty;

        if (jsonExceptionPath[0] != '$')
            return jsonExceptionPath;

        var sb = new StringBuilder(jsonExceptionPath.Length);
        var i = 1;
        while (i < jsonExceptionPath.Length)
        {
            var c = jsonExceptionPath[i];
            if (c == '.')
            {
                i++;
                var start = i;
                while (i < jsonExceptionPath.Length
                       && jsonExceptionPath[i] != '.'
                       && jsonExceptionPath[i] != '[')
                    i++;
                if (i > start)
                {
                    if (sb.Length > 0) sb.Append('.');
                    sb.Append(jsonExceptionPath, start, i - start);
                }
                else
                {
                    sb.Append("[\"\"]");
                }
            }
            else if (c == '[')
            {
                if (i + 1 < jsonExceptionPath.Length && jsonExceptionPath[i + 1] == '\'')
                {
                    var contentStart = i + 2;
                    var closeIdx = -1;
                    for (var j = contentStart; j + 1 < jsonExceptionPath.Length; j++)
                    {
                        if (jsonExceptionPath[j] != '\'') continue;
                        if (jsonExceptionPath[j + 1] != ']') continue;
                        var afterIdx = j + 2;
                        if (afterIdx == jsonExceptionPath.Length
                            || jsonExceptionPath[afterIdx] == '.'
                            || jsonExceptionPath[afterIdx] == '[')
                        {
                            closeIdx = j;
                            break;
                        }
                    }

                    string content;
                    if (closeIdx >= 0)
                    {
                        content = jsonExceptionPath.Substring(contentStart, closeIdx - contentStart);
                        i = closeIdx + 2;
                    }
                    else
                    {
                        content = jsonExceptionPath[contentStart..];
                        i = jsonExceptionPath.Length;
                    }

                    if (content.Length == 0)
                    {
                        sb.Append("[\"\"]");
                    }
                    else
                    {
                        if (sb.Length > 0) sb.Append('.');
                        sb.Append(content);
                    }
                }
                else
                {
                    var start = i;
                    while (i < jsonExceptionPath.Length && jsonExceptionPath[i] != ']') i++;
                    if (i < jsonExceptionPath.Length) i++;
                    sb.Append(jsonExceptionPath, start, i - start);
                }
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Combines a parent MVC key with a translated leaf MVC key per MVC dot+bracket convention.
    /// </summary>
    /// <remarks>
    /// MVC convention is <c>parent.child</c> for property segments and <c>parent[0]</c> /
    /// <c>parent[""]</c> for indexer segments — never <c>parent.[0]</c>. The dot separator is
    /// inserted only when the leaf starts with a property segment (i.e., does NOT start with
    /// <c>'['</c>); for indexer leaves the separator is omitted.
    /// </remarks>
    private static string CombineMvcKeys(string parent, string leaf)
    {
        if (string.IsNullOrEmpty(parent))
            return leaf;

        if (string.IsNullOrEmpty(leaf))
            return parent;

        // MVC indexer keys (e.g., "[0]", "[\"\"]", "[\"name\"]") concatenate without a dot:
        //   parent + "[0]"     -> "parent[0]"
        //   parent + "[\"\"]"  -> "parent[\"\"]"
        // Property segments insert a dot:
        //   parent + "child"   -> "parent.child"
        //   parent + "a[0].b"  -> "parent.a[0].b"
        return leaf[0] == '['
            ? string.Concat(parent, leaf)
            : string.Concat(parent, ".", leaf);
    }
}