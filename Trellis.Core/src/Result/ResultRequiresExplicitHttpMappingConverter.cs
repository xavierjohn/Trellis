namespace Trellis;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Default <see cref="JsonConverterFactory"/> attached to <see cref="Result{TValue}"/>,
/// <see cref="IResult"/>, and <see cref="IResult{TValue}"/> that fails fast when a result is
/// JSON-serialized without going through Trellis's HTTP-mapping path
/// (<c>Result&lt;T&gt;.ToHttpResponse()</c> in <c>Trellis.Asp</c>) or an explicit consumer
/// converter. Without this converter, direct serialization could only emit public status/error
/// metadata such as <c>{"IsSuccess": bool, "IsFailure": bool, "Error": …}</c> — a struct dump
/// with no success value, not a stable wire shape, and not the HTTP problem-details / status-code
/// mapping consumers expect from Trellis.
/// </summary>
/// <remarks>
/// <para>
/// The factory matches <see cref="Result{TValue}"/> AND the public result interfaces
/// (<see cref="IResult"/>, <see cref="IResult{TValue}"/>). The attribute lives on both the
/// struct and the interfaces because <c>[JsonConverter]</c> resolution is by static declared
/// type — a controller signature like <c>Task&lt;IResult&lt;int&gt;&gt; GetAsync()</c> picks
/// up the interface attribute, not the struct attribute (the runtime <c>Result&lt;int&gt;</c>
/// is never consulted for converter resolution).
/// </para>
/// <para>
/// The intended controller / minimal-API pattern is to call <c>.ToHttpResponse()</c> on the
/// result, which returns a <c>Microsoft.AspNetCore.Http.IResult</c> that writes the response
/// body itself (so the <see cref="Result{TValue}"/> struct is never serialized by STJ).
/// Returning a raw <see cref="Result{TValue}"/> from a controller action — and letting MVC's
/// default object formatter pick it up — used to silently produce the struct-dump JSON shape
/// with no HTTP status-code mapping for <see cref="Error"/> cases. With this converter
/// attached, that misuse throws <see cref="NotSupportedException"/> on the first request with
/// an actionable message naming <c>.ToHttpResponse()</c>.
/// </para>
/// <para>
/// <b>AOT-safe.</b> The factory throws directly from <see cref="CreateConverter"/> rather
/// than instantiating a typed throwing converter via <c>MakeGenericType</c> /
/// <c>Activator.CreateInstance</c>. This means no reflection-dependent paths run in Native
/// AOT — consumers get the actionable Trellis message instead of an AOT-generated "native
/// code not available" error before the message can fire.
/// </para>
/// <para>
/// Consumers who legitimately want to serialize a result for logging, IPC, or storage can
/// register a converter (or another <see cref="JsonConverterFactory"/>) in
/// <see cref="JsonSerializerOptions.Converters"/> — option-registered converters take
/// precedence over the type's <see cref="JsonConverterAttribute"/>. The override must match
/// the <b>declared</b> static type of the value being serialized:
/// <list type="bullet">
///   <item>Value declared as <see cref="Result{TValue}"/> → register
///     <c>JsonConverter&lt;Result&lt;T&gt;&gt;</c>.</item>
///   <item>Value declared as <see cref="IResult{TValue}"/> → register
///     <c>JsonConverter&lt;IResult&lt;T&gt;&gt;</c>.</item>
///   <item>Value declared as <see cref="IResult"/> → register
///     <c>JsonConverter&lt;IResult&gt;</c>.</item>
///   <item>Mixed shapes → register a <see cref="JsonConverterFactory"/> whose
///     <c>CanConvert</c> returns <see langword="true"/> for every
///     shape the consumer needs.</item>
/// </list>
/// STJ resolves <c>[JsonConverter]</c> against the static declared type, not the runtime
/// type, so a <c>JsonConverter&lt;Result&lt;T&gt;&gt;</c> alone does not cover
/// <c>IResult&lt;T&gt;</c>-declared properties / return signatures.
/// </para>
/// </remarks>
public sealed class ResultRequiresExplicitHttpMappingConverter : JsonConverterFactory
{
    /// <summary>
    /// Returns <see langword="true"/> for <see cref="Result{TValue}"/> and
    /// <see cref="IResult{TValue}"/> shapes, and for the non-generic <see cref="IResult"/>
    /// interface.
    /// </summary>
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        if (typeToConvert == typeof(IResult))
            return true;

        if (!typeToConvert.IsGenericType)
            return false;

        var def = typeToConvert.GetGenericTypeDefinition();
        return def == typeof(Result<>) || def == typeof(IResult<>);
    }

    /// <summary>
    /// Always throws <see cref="NotSupportedException"/> with the actionable Trellis message.
    /// </summary>
    /// <remarks>
    /// Throwing directly from <see cref="CreateConverter"/> sidesteps the typed-converter
    /// instantiation entirely. The previous design used <c>MakeGenericType</c> +
    /// <c>Activator.CreateInstance</c> to materialise a typed <c>JsonConverter&lt;Result&lt;T&gt;&gt;</c>
    /// that would throw on its first <c>Read</c> / <c>Write</c> call — but those reflection
    /// paths are AOT-unsafe (IL2055 / IL3050) and could fail with a "native code not available"
    /// error before the Trellis message had a chance to surface. Throwing here keeps the
    /// converter AOT-safe and ensures consumers see the actionable Trellis message in every
    /// runtime configuration.
    /// </remarks>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        throw new NotSupportedException(BuildMessage(typeToConvert));
    }

    private static string BuildMessage(Type typeToConvert)
    {
        var declaredShape = FormatDeclaredShape(typeToConvert);
        return $"{declaredShape} cannot be JSON-serialized or deserialized directly. " +
            "Result<T> is a domain disposition — call .ToHttpResponse() (Trellis.Asp) on a controller / minimal-API " +
            "return to map success → 200/201/204 and Error.* → 4xx/5xx with problem-details, or unwrap the value " +
            "via Match / TryGetValue before serialization. " +
            "If you intentionally want to JSON-serialize a result (e.g. for logging or IPC), register a " +
            $"JsonConverter for the declared static type ({declaredShape}) in JsonSerializerOptions.Converters — " +
            "STJ resolves [JsonConverter] against the static declared type, not the runtime type, so a converter " +
            "for one shape does not cover the others. Use a JsonConverterFactory if you need to cover multiple " +
            "result shapes (Result<T>, IResult<T>, IResult) at once.";
    }

    private static string FormatDeclaredShape(Type typeToConvert)
    {
        if (typeToConvert == typeof(IResult))
            return "IResult";

        if (typeToConvert.IsGenericType)
        {
            var def = typeToConvert.GetGenericTypeDefinition();
            var inner = typeToConvert.GetGenericArguments()[0].Name;
            if (def == typeof(Result<>))
                return $"Result<{inner}>";
            if (def == typeof(IResult<>))
                return $"IResult<{inner}>";
        }

        // CanConvert gate prevents this path in practice, but keep a defensible default.
        return typeToConvert.Name;
    }
}
