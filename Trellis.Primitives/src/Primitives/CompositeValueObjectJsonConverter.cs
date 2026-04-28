namespace Trellis.Primitives;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// Convention-based <see cref="JsonConverter{T}"/> for composite value objects.
/// </summary>
/// <typeparam name="T">A composite value object (derives from <see cref="ValueObject"/>) with a
/// public static <c>TryCreate</c> factory.</typeparam>
/// <remarks>
/// <para>
/// <b>Apply via</b> <c>[JsonConverter(typeof(CompositeValueObjectJsonConverter&lt;TSelf&gt;))]</c> on the
/// value object class itself. The attribute is required: without it, ASP.NET Core model binding falls
/// back to default construction and <b>silently bypasses <c>TryCreate</c></b> — inner-field validation
/// never runs and an invalid payload propagates into the domain layer.
/// </para>
/// <para>
/// For the full Domain + API JSON binding + EF Core ownership walkthrough on a multi-field VO
/// (<c>ShippingAddress</c>-style), see <b>Cookbook Recipe 13 — Composite value object end-to-end</b>
/// in <c>docs/docfx_project/api_reference/trellis-api-cookbook.md</c>.
/// </para>
/// <para>
/// Discovery convention: each public read-only instance property declared on <typeparamref name="T"/>
/// becomes a JSON field (camelCase of the property name). The property's "primitive type" is the
/// underlying primitive of an <see cref="IScalarValue{TSelf, TPrimitive}"/> property, or the property's
/// own type when it is already a primitive. <typeparamref name="T"/> must expose
/// <c>public static Result&lt;T&gt; TryCreate(p1, ..., pN[, string? fieldName])</c> where the parameters
/// are the primitive types in the order the properties are declared.
/// </para>
/// <para>
/// On read, the converter populates a value array by JSON property name (case-insensitive), invokes
/// <c>TryCreate</c>, and throws <see cref="TrellisJsonValidationException"/> with the result's display
/// message on failure.
/// </para>
/// <para>
/// This converter uses reflection at first use (results are cached). Native AOT apps must root the
/// closed converter type through <see cref="JsonConverterAttribute"/> or a source-generated context.
/// </para>
/// </remarks>
public sealed class CompositeValueObjectJsonConverter<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)]
T> : JsonConverter<T>
    where T : ValueObject
{
    private static readonly CompositeMetadata Metadata = CompositeMetadata.Build(typeof(T));

    /// <inheritdoc />
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new TrellisJsonValidationException($"Expected JSON object for {typeof(T).Name} value.");

        var values = new object?[Metadata.Properties.Count];
        var seen = new bool[Metadata.Properties.Count];

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var name = reader.GetString();
            reader.Read();

            if (name is not null && Metadata.IndexByJsonName.TryGetValue(name, out var idx))
            {
                values[idx] = ReadPrimitive(ref reader, Metadata.Properties[idx].PrimitiveType, Metadata.Properties[idx].JsonName);
                seen[idx] = true;
            }
            else
            {
                reader.Skip();
            }
        }

        for (var i = 0; i < Metadata.Properties.Count; i++)
        {
            if (!seen[i])
                throw new TrellisJsonValidationException($"Required property '{Metadata.Properties[i].JsonName}' is missing.");
        }

        var result = Metadata.Invoke(values);
        if (!result.TryGetValue(out var value, out var error))
            throw new TrellisJsonValidationException(error.GetDisplayMessage());

        return value;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        foreach (var prop in Metadata.Properties)
        {
            var raw = prop.GetPrimitive(value);
            WritePrimitive(writer, prop.JsonName, raw, prop.PrimitiveType);
        }

        writer.WriteEndObject();
    }

    private static object? ReadPrimitive(ref Utf8JsonReader reader, Type primitiveType, string jsonName)
    {
        try
        {
            if (primitiveType == typeof(string))
            {
                if (reader.TokenType == JsonTokenType.Null)
                    throw new TrellisJsonValidationException(
                        $"Property '{jsonName}' must be {DescribePrimitiveWithArticle(primitiveType)}.");

                return reader.GetString();
            }

            if (primitiveType == typeof(decimal))
                return reader.GetDecimal();
            if (primitiveType == typeof(int))
                return reader.GetInt32();
            if (primitiveType == typeof(long))
                return reader.GetInt64();
            if (primitiveType == typeof(short))
                return reader.GetInt16();
            if (primitiveType == typeof(byte))
                return reader.GetByte();
            if (primitiveType == typeof(double))
                return reader.GetDouble();
            if (primitiveType == typeof(float))
                return reader.GetSingle();
            if (primitiveType == typeof(bool))
                return reader.GetBoolean();
            if (primitiveType == typeof(Guid))
                return reader.GetGuid();
            if (primitiveType == typeof(DateTime))
                return reader.GetDateTime();
            if (primitiveType == typeof(DateTimeOffset))
                return reader.GetDateTimeOffset();
        }
        catch (FormatException)
        {
            throw new TrellisJsonValidationException(
                $"Property '{jsonName}' is not a valid {DescribePrimitive(primitiveType)}.");
        }
        catch (InvalidOperationException)
        {
            throw new TrellisJsonValidationException(
                $"Property '{jsonName}' must be {DescribePrimitiveWithArticle(primitiveType)}.");
        }

        throw new TrellisJsonValidationException(
            $"Composite value object '{typeof(T).Name}' uses unsupported primitive '{primitiveType.Name}' for property '{jsonName}'.");
    }

    private static string DescribePrimitive(Type primitiveType)
    {
        if (primitiveType == typeof(string)) return "string";
        if (primitiveType == typeof(decimal) || primitiveType == typeof(double) || primitiveType == typeof(float)) return "number";
        if (primitiveType == typeof(int) || primitiveType == typeof(long) || primitiveType == typeof(short) || primitiveType == typeof(byte)) return "integer";
        if (primitiveType == typeof(bool)) return "boolean";
        if (primitiveType == typeof(Guid)) return "GUID";
        if (primitiveType == typeof(DateTime)) return "date-time";
        if (primitiveType == typeof(DateTimeOffset)) return "date-time with offset";
        return primitiveType.Name;
    }

    private static string DescribePrimitiveWithArticle(Type primitiveType)
    {
        var d = DescribePrimitive(primitiveType);
        return (d.Length > 0 && "aeiouAEIOU".Contains(d[0])) ? "an " + d : "a " + d;
    }

    private static void WritePrimitive(Utf8JsonWriter writer, string jsonName, object? raw, Type primitiveType)
    {
        if (raw is null)
        {
            writer.WriteNull(jsonName);
            return;
        }

        if (primitiveType == typeof(string))
        {
            writer.WriteString(jsonName, raw is string text
                ? text
                : Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (primitiveType == typeof(decimal))
        {
            writer.WriteNumber(jsonName, (decimal)raw);
        }
        else if (primitiveType == typeof(int))
        {
            writer.WriteNumber(jsonName, (int)raw);
        }
        else if (primitiveType == typeof(long))
        {
            writer.WriteNumber(jsonName, (long)raw);
        }
        else if (primitiveType == typeof(short))
        {
            writer.WriteNumber(jsonName, (short)raw);
        }
        else if (primitiveType == typeof(byte))
        {
            writer.WriteNumber(jsonName, (byte)raw);
        }
        else if (primitiveType == typeof(double))
        {
            writer.WriteNumber(jsonName, (double)raw);
        }
        else if (primitiveType == typeof(float))
        {
            writer.WriteNumber(jsonName, (float)raw);
        }
        else if (primitiveType == typeof(bool))
        {
            writer.WriteBoolean(jsonName, (bool)raw);
        }
        else if (primitiveType == typeof(Guid))
        {
            writer.WriteString(jsonName, (Guid)raw);
        }
        else if (primitiveType == typeof(DateTime))
        {
            writer.WriteString(jsonName, (DateTime)raw);
        }
        else if (primitiveType == typeof(DateTimeOffset))
        {
            writer.WriteString(jsonName, (DateTimeOffset)raw);
        }
        else
        {
            throw new TrellisJsonValidationException(
                $"Unsupported primitive type '{primitiveType}' for JSON property '{jsonName}'.");
        }
    }

    [SuppressMessage("Design", "CA1812", Justification = "Instantiated via static constructor.")]
    private sealed class PropertyMetadata
    {
        public required string PropertyName { get; init; }

        public required string JsonName { get; init; }

        public required Type PrimitiveType { get; init; }

        public required Func<T, object?> GetPrimitive { get; init; }
    }

    [SuppressMessage("Design", "CA1812", Justification = "Instantiated via static constructor.")]
    private sealed class CompositeMetadata
    {
        public required List<PropertyMetadata> Properties { get; init; }

        public required Dictionary<string, int> IndexByJsonName { get; init; }

        public required Func<object?[], Result<T>> Invoke { get; init; }

        [UnconditionalSuppressMessage("Trimming", "IL2072",
            Justification = "Composite VO public properties are preserved by the converter generic parameter annotation; property type interface annotations are not expressible through PropertyInfo.")]
        public static CompositeMetadata Build(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)]
            Type type)
        {
            var discoveredProperties = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p =>
                    p.GetMethod is not null &&
                    p.GetIndexParameters().Length == 0 &&
                    (p.SetMethod is null || !p.SetMethod.IsPublic))
                .ToList();

            var properties = OrderProperties(type, discoveredProperties);

            var props = new List<PropertyMetadata>(properties.Count);
            foreach (var p in properties)
            {
                var primitive = GetPrimitiveType(p.PropertyType);
                var getter = BuildPrimitiveGetter(p, primitive);
                props.Add(new PropertyMetadata
                {
                    PropertyName = p.Name,
                    JsonName = JsonNamingPolicy.CamelCase.ConvertName(p.Name),
                    PrimitiveType = primitive,
                    GetPrimitive = getter,
                });
            }

            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < props.Count; i++)
                index[props[i].JsonName] = i;

            var invoker = BuildInvoker(type, props);

            return new CompositeMetadata
            {
                Properties = props,
                IndexByJsonName = index,
                Invoke = invoker,
            };
        }

        [UnconditionalSuppressMessage("Trimming", "IL2072",
            Justification = "Property types are discovered from a rooted composite value object. Scalar value object interfaces are preserved by their generated converter/model-binding surface.")]
        private static Type GetPrimitiveType(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
            Type propertyType)
        {
            foreach (var iface in propertyType.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IScalarValue<,>))
                    return iface.GetGenericArguments()[1];
            }

            return propertyType;
        }

        private static List<PropertyInfo> OrderProperties(Type type, List<PropertyInfo> properties)
        {
            var withTokens = new List<(PropertyInfo Property, int Token)>(properties.Count);
            foreach (var property in properties)
            {
                if (!TryGetMetadataToken(property, out var token))
                    return OrderPropertiesWithoutMetadataTokens(type, properties);

                withTokens.Add((property, token));
            }

            return withTokens
                .OrderBy(p => p.Token)
                .Select(p => p.Property)
                .ToList();
        }

        [UnconditionalSuppressMessage("Trimming", "IL2072",
            Justification = "Property types are discovered from a rooted composite value object. The fallback only inspects interfaces to reject unsafe duplicate primitive shapes before any value conversion occurs.")]
        private static List<PropertyInfo> OrderPropertiesWithoutMetadataTokens(Type type, List<PropertyInfo> properties)
        {
            var duplicatePrimitiveTypes = properties
                .GroupBy(p => GetPrimitiveType(p.PropertyType))
                .Where(g => g.Count() > 1)
                .Select(g => g.Key.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            if (duplicatePrimitiveTypes.Length > 0)
            {
                throw new InvalidOperationException(
                    $"CompositeValueObjectJsonConverter<{type.Name}> cannot determine a safe property order because metadata tokens are unavailable " +
                    $"and multiple properties share the same primitive type(s): {string.Join(", ", duplicatePrimitiveTypes)}. " +
                    "Use a hand-written converter for this composite value object.");
            }

            return properties
                .OrderBy(p => GetPrimitiveType(p.PropertyType).Name, StringComparer.Ordinal)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToList();
        }

        private static bool TryGetMetadataToken(MemberInfo member, out int metadataToken)
        {
            try
            {
                metadataToken = member.MetadataToken;
                return true;
            }
            catch (InvalidOperationException)
            {
                metadataToken = 0;
                return false;
            }
        }

        [UnconditionalSuppressMessage("Trimming", "IL2075",
            Justification = "Property types are discovered from a rooted composite value object. Scalar value object Value members are part of the public Trellis scalar contract.")]
        private static Func<T, object?> BuildPrimitiveGetter(PropertyInfo propInfo, Type primitiveType)
        {
            var instance = Expression.Parameter(typeof(T), "v");
            Expression body = Expression.Property(instance, propInfo);

            if (propInfo.PropertyType == primitiveType)
            {
                body = Expression.Convert(body, typeof(object));
            }
            else
            {
                var valueProp = propInfo.PropertyType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                body = valueProp is not null && valueProp.PropertyType == primitiveType
                    ? BuildScalarValueAccess(body, propInfo.PropertyType, valueProp)
                    : Expression.Convert(body, typeof(object));
            }

            return Expression.Lambda<Func<T, object?>>(body, instance).Compile();
        }

        private static Expression BuildScalarValueAccess(Expression body, Type propertyType, PropertyInfo valueProp) =>
            propertyType.IsValueType
                ? Expression.Convert(Expression.Property(body, valueProp), typeof(object))
                : Expression.Condition(
                    Expression.Equal(body, Expression.Constant(null, propertyType)),
                    Expression.Constant(null, typeof(object)),
                    Expression.Convert(Expression.Property(body, valueProp), typeof(object)));

        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "Result<T> is constructed via MakeGenericType to match the TryCreate factory's return type. T is the converter's owning type and is reachable through JsonConverter<T>. The class XML doc directs AOT consumers to hand-write a converter; the source-generator extension is tracked as a follow-up.")]
        private static Func<object?[], Result<T>> BuildInvoker(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
            Type type,
            List<PropertyMetadata> props)
        {
            var resultType = typeof(Result<>).MakeGenericType(type);
            var primitiveTypes = props.Select(p => p.PrimitiveType).ToArray();

            MethodInfo? match = null;
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "TryCreate")
                    continue;
                if (m.ReturnType != resultType)
                    continue;

                var parameters = m.GetParameters();
                if (parameters.Length < primitiveTypes.Length)
                    continue;

                var prefixMatches = true;
                for (var i = 0; i < primitiveTypes.Length; i++)
                {
                    if (parameters[i].ParameterType != primitiveTypes[i])
                    {
                        prefixMatches = false;
                        break;
                    }
                }

                if (!prefixMatches)
                    continue;

                var trailingAllOptional = true;
                for (var i = primitiveTypes.Length; i < parameters.Length; i++)
                {
                    if (!parameters[i].IsOptional)
                    {
                        trailingAllOptional = false;
                        break;
                    }
                }

                if (!trailingAllOptional)
                    continue;

                if (match is not null)
                {
                    throw new InvalidOperationException(
                        $"CompositeValueObjectJsonConverter<{type.Name}> found multiple ambiguous 'TryCreate' overloads matching parameters " +
                        $"[{string.Join(", ", primitiveTypes.Select(t => t.Name))}]. Define a single unambiguous overload " +
                        $"(exact parameter count or distinct prefix) returning 'Result<{type.Name}>'.");
                }

                match = m;
            }

            if (match is null)
            {
                throw new InvalidOperationException(
                    $"CompositeValueObjectJsonConverter<{type.Name}> requires a public static 'TryCreate' returning 'Result<{type.Name}>' " +
                    $"with parameters [{string.Join(", ", primitiveTypes.Select(t => t.Name))}] (followed by optional parameters only).");
            }

            var allParams = match.GetParameters();
            var valuesParam = Expression.Parameter(typeof(object?[]), "v");
            var args = new Expression[allParams.Length];
            for (var i = 0; i < allParams.Length; i++)
            {
                args[i] = i < primitiveTypes.Length
                    ? Expression.Convert(
                        Expression.ArrayIndex(valuesParam, Expression.Constant(i)),
                        allParams[i].ParameterType)
                    : Expression.Constant(allParams[i].DefaultValue, allParams[i].ParameterType);
            }

            var call = Expression.Call(match, args);
            return Expression.Lambda<Func<object?[], Result<T>>>(call, valuesParam).Compile();
        }
    }
}