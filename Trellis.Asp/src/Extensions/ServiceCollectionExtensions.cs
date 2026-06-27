namespace Trellis.Asp;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Trellis;
using Trellis.Asp.ModelBinding;
using Trellis.Asp.Validation;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

/// <summary>
/// Extension methods for configuring automatic value object validation in ASP.NET Core.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds automatic validation for scalar value types during
    /// model binding and JSON deserialization.
    /// </summary>
    /// <param name="builder">The <see cref="IMvcBuilder"/>.</param>
    /// <returns>The <see cref="IMvcBuilder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method configures ASP.NET Core to automatically validate scalar values that implement
    /// <see cref="IScalarValue{TSelf, TPrimitive}"/> during:
    /// <list type="bullet">
    /// <item><strong>Model binding:</strong> Values from route, query, form, or headers</item>
    /// <item><strong>JSON deserialization:</strong> Values from request body (with error collection)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Unlike traditional validation that throws on first error, this approach:
    /// <list type="bullet">
    /// <item>Collects ALL validation errors during JSON deserialization</item>
    /// <item>Uses property names (not JSON paths) in error messages</item>
    /// <item>Returns comprehensive 400 Bad Request with all field errors</item>
    /// <item>Integrates seamlessly with <c>[ApiController]</c> attribute</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// Registration in Program.cs:
    /// <code>
    /// using Trellis;
    ///
    /// var builder = WebApplication.CreateBuilder(args);
    ///
    /// builder.Services
    ///     .AddControllers()
    ///     .AddScalarValueValidation();
    ///
    /// var app = builder.Build();
    /// app.MapControllers();
    /// app.Run();
    /// </code>
    /// </example>
    /// <example>
    /// Usage in controllers with automatic validation:
    /// <code>
    /// public record RegisterUserDto
    /// {
    ///     public EmailAddress Email { get; init; } = null!;
    ///     public FirstName FirstName { get; init; } = null!;
    /// }
    ///
    /// [ApiController]
    /// [Route("api/users")]
    /// public class UsersController : ControllerBase
    /// {
    ///     [HttpPost]
    ///     public ActionResult&lt;User&gt; Register(RegisterUserDto dto)
    ///     {
    ///         // If we reach here, dto is fully validated!
    ///         // All scalar values passed validation
    ///
    ///         var user = User.TryCreate(dto.Email, dto.FirstName);
    ///         return user.ToHttpResponse().AsActionResult&lt;User&gt;();
    ///     }
    /// }
    /// </code>
    /// </example>
    public static IMvcBuilder AddScalarValueValidation(this IMvcBuilder builder)
    {
        // Delegate to the IServiceCollection overload so that callers chaining
        // AddControllers().AddScalarValueValidation() get the same idempotent registration
        // path as callers using the simpler AddTrellisAsp() entry point.
        builder.Services.AddScalarValueValidation();
        return builder;
    }

    /// <summary>
    /// Configures JSON serializer options to use property-aware value object validation.
    /// </summary>
    /// <param name="options">The JSON serializer options to configure.</param>
    /// <remarks>
    /// This method configures a type info modifier that assigns converters per-property,
    /// ensuring that validation error field names match the C# property names.
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "DefaultJsonTypeInfoResolver is only used as a fallback when the caller has not supplied a TypeInfoResolver. AOT consumers must supply a JsonSerializerContext-backed resolver before calling AddScalarValueValidation*; this is documented on the public extension methods.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "DefaultJsonTypeInfoResolver is only used as a fallback when the caller has not supplied a TypeInfoResolver. AOT consumers must supply a JsonSerializerContext-backed resolver before calling AddScalarValueValidation*; this is documented on the public extension methods.")]
    private static void ConfigureJsonOptions(JsonSerializerOptions options)
    {
        // Use TypeInfoResolver modifier to assign converters per-property with property name.
        // When the caller hasn't configured a resolver (typical for non-AOT apps), fall back
        // to DefaultJsonTypeInfoResolver. AOT consumers should configure a source-generated
        // JsonSerializerContext resolver before calling AddScalarValueValidation*.
        var existingResolver = options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
        options.TypeInfoResolver = existingResolver.WithAddedModifier(ModifyTypeInfo);

        // Add factories for direct serialization scenarios in reflection-enabled apps.
        // Native AOT source-generated contexts cannot expand reflection-created converter factories;
        // AOT consumers should make scalar roots reachable through their JsonSerializerContext.
        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            options.Converters.Add(new ValidatingJsonConverterFactory());
            options.Converters.Add(new MaybeScalarValueJsonConverterFactory());
            options.Converters.Add(new MaybePrimitiveJsonConverterFactory());
        }
    }

    /// <summary>
    /// Modifies type info to inject property names into ValidationErrorsContext before deserialization
    /// and adds post-deserialization checks for missing non-nullable scalar value object properties.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "PropertyType comes from JSON serialization infrastructure which preserves type information")]
    private static void ModifyTypeInfo(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        // Track non-nullable scalar VO properties for post-deserialization null checks.
        // When a JSON property is missing entirely, STJ never invokes the converter —
        // the property silently defaults to null. This is especially misleading for
        // positional records where constructor parameters look required but are still
        // nullable reference types at the CLR level.
        List<(string Name, Func<object, object?> Get)>? requiredScalarProperties = null;

        foreach (var property in typeInfo.Properties)
        {
            var propertyType = property.PropertyType;

            // Check for Maybe<TValue> where TValue : IScalarValue<TValue, TPrimitive>
            if (ScalarValueTypeHelper.IsMaybeScalarValue(propertyType))
            {
                var innerConverter = CreateMaybeConverter(propertyType);
                if (innerConverter is null)
                    continue;

                var wrappedConverter = CreatePropertyNameAwareConverter(innerConverter, property.Name, propertyType);
                if (wrappedConverter is not null)
                    property.CustomConverter = wrappedConverter;

                continue;
            }

            // Direct scalar value object (IScalarValue<TSelf, T>)?
            if (IsScalarValueProperty(property))
            {
                // Create a validating converter for this value object
                var innerScalarConverter = CreateValidatingConverter(propertyType);
                if (innerScalarConverter is null)
                    continue;

                // Wrap it with property name awareness. In Native AOT this returns null because
                // runtime closed-generic converter construction is disabled; source-generated
                // converters are expected to own scalar JSON conversion there.
                var wrappedScalarConverter = CreatePropertyNameAwareConverter(innerScalarConverter, property.Name, propertyType);
                if (wrappedScalarConverter is not null)
                    property.CustomConverter = wrappedScalarConverter;

                // Track non-nullable scalar VO properties for missing-property detection
                if (!property.IsGetNullable && property.Get is not null)
                {
                    requiredScalarProperties ??= [];
                    requiredScalarProperties.Add((property.Name, property.Get));
                }

                continue;
            }

            // A container property (collection or nested object) whose graph transitively contains a
            // scalar value object. Wrapping it pushes the property name (and, for collections, the
            // element index) onto the validation ancestor path so a nested value-object failure reports
            // an index-precise field path (e.g. /members/0/email) instead of just the leaf name.
            // Respect an explicit property-level converter (e.g. a [JsonConverter] attribute): only
            // install the wrapper when the property uses default, type-based conversion, so a consumer's
            // converter is never silently bypassed.
            if (property.CustomConverter is not null)
                continue;

            var containerConverter = CreatePathTrackingContainerConverter(property);
            if (containerConverter is not null)
                property.CustomConverter = containerConverter;
        }

        // Add post-deserialization callback to detect missing required scalar VO properties.
        // Compose with any existing OnDeserialized callback to avoid overwriting consumer behavior.
        if (requiredScalarProperties is not null)
        {
            var existingOnDeserialized = typeInfo.OnDeserialized;
            typeInfo.OnDeserialized = obj =>
            {
                existingOnDeserialized?.Invoke(obj);

                foreach (var (name, get) in requiredScalarProperties)
                {
                    if (get(obj) is not null)
                        continue;

                    // Only add an error if the converter didn't already report one
                    // (explicit null tokens are handled by ValidatingJsonConverter)
                    if (ValidationErrorsContext.Current is not null
                        && !HasExistingErrorForField(name))
                    {
                        ValidationErrorsContext.AddError(name, $"'{name}' is required.");
                    }
                }
            };
        }
    }

    /// <summary>
    /// Checks whether a validation error has already been collected for the given field name.
    /// Used to avoid double-reporting when an explicit JSON null was already caught by the converter.
    /// </summary>
    private static bool HasExistingErrorForField(string fieldName) =>
        ValidationErrorsContext.HasErrorForField(fieldName);

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "PropertyType comes from JSON serialization infrastructure which preserves type information")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "PropertyType comes from JSON serialization infrastructure which preserves type information")]
    private static bool IsScalarValueProperty(JsonPropertyInfo property) =>
        ScalarValueTypeHelper.IsScalarValue(property.PropertyType);

    private static JsonConverter? CreateValidatingConverter([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type valueType)
    {
        var primitiveType = ScalarValueTypeHelper.GetPrimitiveType(valueType);
        return primitiveType is null
            ? null
            : ScalarValueTypeHelper.CreateGenericInstance<JsonConverter>(
                typeof(ValidatingJsonConverter<,>),
                valueType,
                primitiveType);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Value object types are preserved by JSON serialization infrastructure")]
    private static JsonConverter? CreateMaybeConverter(Type maybeType)
    {
        var innerType = ScalarValueTypeHelper.GetMaybeInnerType(maybeType);
        if (innerType is null)
            return null;

        var primitiveType = ScalarValueTypeHelper.GetPrimitiveType(innerType);
        return primitiveType is null
            ? null
            : ScalarValueTypeHelper.CreateGenericInstance<JsonConverter>(
                typeof(MaybeScalarValueJsonConverter<,>),
                innerType,
                primitiveType);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Guarded by RuntimeFeature.IsDynamicCodeSupported; Native AOT returns null before constructing a closed generic wrapper.")]
    [UnconditionalSuppressMessage("Trimming", "IL2055",
        Justification = "Reflection-enabled fallback only. PropertyNameAwareConverter<T> is constructed only for property types already present in JSON serialization metadata.")]
    private static JsonConverter? CreatePropertyNameAwareConverter(JsonConverter innerConverter, string propertyName, Type type)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return null;

        var wrapperType = typeof(PropertyNameAwareConverter<>).MakeGenericType(type);
        return Activator.CreateInstance(wrapperType, innerConverter, propertyName) as JsonConverter;
    }

    // Wraps a container property (collection or nested object) whose graph transitively contains a
    // scalar value object so its path segment(s) are pushed during deserialization. Returns null under
    // Native AOT (no runtime generic construction) and for containers with no value object inside.
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Guarded by RuntimeFeature.IsDynamicCodeSupported; Native AOT returns null before constructing a closed generic wrapper.")]
    [UnconditionalSuppressMessage("Trimming", "IL2055",
        Justification = "Reflection-enabled fallback only; constructed for property/element types already present in JSON serialization metadata.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Property and element types come from JSON serialization metadata which preserves type information.")]
    private static JsonConverter? CreatePathTrackingContainerConverter(JsonPropertyInfo property)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return null;

        var propertyType = property.PropertyType;
        if (!ContainsScalarValueTransitively(propertyType, []))
            return null;

        var elementType = GetWrappableCollectionElementType(propertyType);
        if (elementType is not null)
        {
            var collectionWrapper = typeof(PathTrackingCollectionConverter<,>).MakeGenericType(propertyType, elementType);
            return Activator.CreateInstance(collectionWrapper, property.Name) as JsonConverter;
        }

        if (!IsUserObjectType(propertyType))
            return null;

        // Nested object: the wrapper pushes the property name and deserializes the object through
        // JsonSerializer (its own type converter), so a self-referential DTO graph cannot re-enter
        // metadata resolution while this converter is being constructed.
        var objectWrapper = typeof(PathTrackingObjectConverter<>).MakeGenericType(propertyType);
        return Activator.CreateInstance(objectWrapper, property.Name) as JsonConverter;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "Reflection-mode-only DTO graph walk; ScalarValueTypeHelper.IsScalarValue inspects interfaces preserved by JSON metadata.")]
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Walks the public properties of user DTO types to decide whether to install a path-tracking wrapper; reflection-mode fallback only.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Walks the public properties of user DTO types to decide whether to install a path-tracking wrapper; reflection-mode fallback only.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Walks the public properties of user DTO types to decide whether to install a path-tracking wrapper; reflection-mode fallback only.")]
    private static bool ContainsScalarValueTransitively(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
            return false;

        if (ScalarValueTypeHelper.IsScalarValue(type) || ScalarValueTypeHelper.IsMaybeScalarValue(type))
            return true;

        var element = GetEnumerableElementType(type);
        if (element is not null)
            return ContainsScalarValueTransitively(element, visited);

        if (!IsUserObjectType(type))
            return false;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length != 0)
                continue;

            if (ContainsScalarValueTransitively(prop.PropertyType, visited))
                return true;
        }

        return false;
    }

    // The element type to wrap for index tracking — only when a List<element> is assignable back to the
    // property type (List<T> itself, an array, or the IList/ICollection/IEnumerable/IReadOnlyList/
    // IReadOnlyCollection<T> interfaces). Other collection shapes are left to STJ (leaf-only paths).
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Reflection-mode-only; reached only when RuntimeFeature.IsDynamicCodeSupported via CreatePathTrackingContainerConverter.")]
    [UnconditionalSuppressMessage("Trimming", "IL2055",
        Justification = "Reflection-mode-only; List<element> is built only to test assignability to the property type already present in JSON metadata.")]
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Reflection-mode-only inspection of collection property types from JSON metadata.")]
    private static Type? GetWrappableCollectionElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        var element = GetEnumerableElementType(type);
        if (element is null)
            return null;

        var listType = typeof(List<>).MakeGenericType(element);
        return type.IsAssignableFrom(listType) ? element : null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Inspects generic IEnumerable<T> interfaces on collection property types from JSON metadata; reflection-mode fallback only.")]
    private static Type? GetEnumerableElementType(Type type)
    {
        if (type == typeof(string))
            return null;

        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return type.GetGenericArguments()[0];

        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return iface.GetGenericArguments()[0];
        }

        return null;
    }

    private static bool IsUserObjectType(Type type) =>
        type is { IsClass: true, IsArray: false }
        && type != typeof(string)
        && type != typeof(object)
        && (type.Namespace is null || !type.Namespace.StartsWith("System", StringComparison.Ordinal));

    /// <summary>
    /// Adds automatic value object validation for both MVC Controllers and Minimal APIs.
    /// This is a convenience method that configures validation for all ASP.NET Core application types.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method is equivalent to calling both:
    /// <list type="bullet">
    /// <item><see cref="AddScalarValueValidation(IMvcBuilder)"/> for MVC JSON options</item>
    /// <item><see cref="AddScalarValueValidationForMinimalApi"/> for Minimal API JSON options</item>
    /// </list>
    /// </para>
    /// <para>
    /// Use this method when you have both controllers and Minimal API endpoints in your application,
    /// or when you're not sure which style you'll use. For applications using only one approach,
    /// prefer the specific methods for clarity.
    /// </para>
    /// <para>
    /// <strong>Note:</strong> In addition to JSON options, this method also configures the
    /// MVC binding/validation pipeline (<see cref="ScalarValueModelBinderProvider"/>,
    /// <see cref="MaybeSuppressChildValidationMetadataProvider"/>,
    /// <see cref="ScalarValueValidationFilter"/>, and <c>SuppressModelStateInvalidFilter</c>),
    /// so MVC controller hosts that call only this method (or the convenience helper
    /// <see cref="AddTrellisAspWithScalarValidation(IServiceCollection)"/>) still get the full
    /// scalar-value-object validation experience without having to chain
    /// <c>AddControllers().AddScalarValueValidation()</c>. The MVC registrations are no-ops
    /// when controllers are not added, and are idempotent so combining both calls is safe.
    /// </para>
    /// <para>
    /// <strong>Minimal API endpoint filter and middleware are NOT registered by this method.</strong>
    /// Minimal API hosts must additionally call <c>app.UseScalarValueValidation()</c> (middleware)
    /// and chain <c>.WithScalarValueValidation()</c> on each endpoint that should surface
    /// validation as RFC 9457 ProblemDetails.
    /// </para>
    /// </remarks>
    /// <example>
    /// Simple setup for mixed applications:
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    ///
    /// // Unified setup - works for both MVC and Minimal APIs
    /// builder.Services.AddControllers();
    /// builder.Services.AddScalarValueValidation();
    ///
    /// var app = builder.Build();
    ///
    /// app.UseScalarValueValidation();
    /// app.MapControllers();
    /// app.MapPost("/api/users", (RegisterDto dto) => ...).WithScalarValueValidation();
    ///
    /// app.Run();
    /// </code>
    /// </example>
    /// <example>
    /// For MVC-only applications, prefer the fluent API:
    /// <code>
    /// builder.Services
    ///     .AddControllers()
    ///     .AddScalarValueValidation(); // ← Better for MVC-only apps
    /// </code>
    /// </example>
    public static IServiceCollection AddScalarValueValidation(this IServiceCollection services)
    {
        // Configure MVC JSON options (for controllers)
        services.Configure<MvcJsonOptions>(options =>
            ConfigureJsonOptions(options.JsonSerializerOptions));

        // Configure Minimal API JSON options
        services.ConfigureHttpJsonOptions(options =>
            ConfigureJsonOptions(options.SerializerOptions));

        // Configure MVC-specific binding/validation pipeline for controllers.
        // These Configure<MvcOptions>/Configure<ApiBehaviorOptions> registrations are no-ops
        // when AddControllers() is never called, but ensure a controller-using host that calls
        // only AddTrellisAsp() (without chaining .AddScalarValueValidation() on AddControllers())
        // still gets MaybeSuppressChildValidationMetadataProvider, the scalar model binder, the
        // scalar validation filter, and SuppressModelStateInvalidFilter — matching what Recipe 14
        // and the public docs promise.
        //
        // The inner callbacks are idempotent so repeated registration (e.g. AddTrellisAsp() plus
        // AddControllers().AddScalarValueValidation()) does not duplicate filters/providers.
        services.Configure<MvcOptions>(options =>
        {
            if (!options.ModelBinderProviders.Any(p => p is ScalarValueModelBinderProvider))
                options.ModelBinderProviders.Insert(0, new ScalarValueModelBinderProvider());

            if (!options.ModelMetadataDetailsProviders.Any(p => p is MaybeSuppressChildValidationMetadataProvider))
                options.ModelMetadataDetailsProviders.Add(new MaybeSuppressChildValidationMetadataProvider());

            if (!options.Filters.Any(f =>
                    (f as TypeFilterAttribute)?.ImplementationType == typeof(ScalarValueValidationFilter)
                    || f is ScalarValueValidationFilter))
                options.Filters.Add<ScalarValueValidationFilter>();
        });
        services.Configure<ApiBehaviorOptions>(options =>
            options.SuppressModelStateInvalidFilter = true);

        // MVC's SystemTextJsonInputFormatter, by default, wraps a caught JsonException in
        // an InputFormatterException ("safe to expose" marker). ModelStateDictionary then
        // takes the string-only path for InputFormatterException, dropping the original
        // exception object and its inner exception chain. That destroys our structured
        // payload — TrellisJsonValidationException.InvalidInput — which the
        // ScalarValueValidationFilter needs to render per-field wire entries instead of
        // a single ;-joined string under the parent path. Disabling the wrap preserves the
        // exception object in ModelState. The user-visible ErrorMessage on the wire is
        // unchanged because ModelStateDictionary still derives it from exception.Message
        // when storing the exception.
        services.Configure<MvcJsonOptions>(options =>
            options.AllowInputFormatterExceptionMessages = false);

        return services;
    }

    /// <summary>
    /// Adds middleware that creates a validation error collection scope for each request.
    /// This middleware must be registered in the pipeline to enable validation error collection.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This middleware creates a <see cref="ValidationErrorsContext"/> scope for each request,
    /// allowing <see cref="ValidatingJsonConverter{TValue,TPrimitive}"/> to collect
    /// validation errors during JSON deserialization.
    /// </para>
    /// <para>
    /// Register this middleware early in the pipeline, before any middleware that deserializes
    /// JSON request bodies (such as routing or MVC).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var app = builder.Build();
    ///
    /// app.UseScalarValueValidation(); // ← Add this before routing
    /// app.UseRouting();
    /// app.UseAuthentication();
    /// app.UseAuthorization();
    /// app.MapControllers();
    ///
    /// app.Run();
    /// </code>
    /// </example>
    public static IApplicationBuilder UseScalarValueValidation(this IApplicationBuilder app) =>
        app.UseMiddleware<ScalarValueValidationMiddleware>();

    /// <summary>
    /// Configures HTTP JSON options to use property-aware value object validation for Minimal APIs.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method configures Minimal API JSON serialization to automatically validate value objects
    /// that implement <see cref="IScalarValue{TSelf, TPrimitive}"/> during JSON deserialization.
    /// </para>
    /// <para>
    /// For Minimal APIs, also use <see cref="UseScalarValueValidation"/> middleware and
    /// <see cref="WithScalarValueValidation"/> on your route handlers.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddScalarValueValidationForMinimalApi();
    ///
    /// var app = builder.Build();
    /// app.UseScalarValueValidation();
    ///
    /// app.MapPost("/users", (RegisterUserDto dto) => ...)
    ///    .WithScalarValueValidation();
    /// </code>
    /// </example>
    public static IServiceCollection AddScalarValueValidationForMinimalApi(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
            ConfigureJsonOptions(options.SerializerOptions));
        return services;
    }

    /// <summary>
    /// Adds the value object validation endpoint filter to the route handler.
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <returns>The route handler builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This extension adds <see cref="ScalarValueValidationEndpointFilter"/> to check for
    /// validation errors collected during JSON deserialization.
    /// </para>
    /// <para>
    /// Ensure <see cref="UseScalarValueValidation"/> middleware is registered and
    /// <see cref="AddScalarValueValidationForMinimalApi"/> is called for full functionality.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// app.MapPost("/users/register", (RegisterUserDto dto) =>
    /// {
    ///     // dto is already validated
    ///     return Results.Ok(dto);
    /// }).WithScalarValueValidation();
    /// </code>
    /// </example>
    public static RouteHandlerBuilder WithScalarValueValidation(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ScalarValueValidationEndpointFilter>();

    /// <summary>
    /// Registers Trellis ASP.NET Core integration with default error-to-HTTP-status-code mappings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method uses the default error-to-status-code mappings. Most teams can use this
    /// zero-configuration overload. Call the overload accepting <see cref="Action{TrellisAspOptions}"/>
    /// to customize specific mappings.
    /// </para>
    /// <para>
    /// <b>Does NOT register scalar-value validation.</b> If your endpoints accept request bodies
    /// containing Trellis value objects (<see cref="IScalarValue{TSelf, TPrimitive}"/>,
    /// <see cref="Maybe{T}"/>) bound from JSON / route / query, you must additionally call
    /// <see cref="AddScalarValueValidation(IServiceCollection)"/> — or use the convenience
    /// helper <see cref="AddTrellisAspWithScalarValidation(IServiceCollection)"/> which composes
    /// both registrations. The split exists so the global <c>MvcOptions</c> / <c>JsonOptions</c>
    /// mutation that scalar validation performs is opt-in instead of silent.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Error mapping only (no scalar-value JSON / model-binding wiring):
    /// builder.Services.AddTrellisAsp();
    ///
    /// // Greenfield default — error mapping + scalar-value validation:
    /// builder.Services.AddTrellisAspWithScalarValidation();
    /// </code>
    /// </example>
    public static IServiceCollection AddTrellisAsp(this IServiceCollection services)
        => services.AddTrellisAsp(_ => { });

    /// <summary>
    /// Registers Trellis ASP.NET Core integration with custom error-to-HTTP-status-code mappings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure error-to-HTTP-status-code mappings.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Default mappings are applied first. The <paramref name="configure"/> action can override
    /// any mapping using <see cref="TrellisAspOptions.MapError{TError}"/>.
    /// </para>
    /// <para>
    /// <b>Composition:</b> multiple <c>AddTrellisAsp(...)</c> calls compose. Each call's
    /// <paramref name="configure"/> delegate runs (in registration order) against the same
    /// <see cref="TrellisAspOptions"/> instance materialized by
    /// <c>OptionsFactory&lt;TrellisAspOptions&gt;</c>, so mappings for distinct
    /// <c>TError</c> types from earlier calls survive. Mappings for the same
    /// <c>TError</c> follow last-wins.
    /// </para>
    /// <para>
    /// <b>Slot ownership:</b> <c>AddTrellisAsp</c> owns the <see cref="TrellisAspOptions"/>
    /// service descriptor and replaces any existing one with a bridge factory that resolves
    /// from <c>IOptions&lt;TrellisAspOptions&gt;.Value</c>. Hosts must customize via this
    /// <paramref name="configure"/> action; raw <c>services.AddSingleton(new TrellisAspOptions())</c>
    /// is unsupported and will be silently overwritten the next time <c>AddTrellisAsp</c> runs.
    /// </para>
    /// <para>
    /// <b>Does NOT register scalar-value validation.</b> See the parameterless overload's
    /// remarks for the rationale; use
    /// <see cref="AddTrellisAspWithScalarValidation(IServiceCollection, Action{TrellisAspOptions})"/>
    /// when you also want VO model binding / JSON converter wiring.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddTrellisAsp(options =>
    /// {
    ///     options.MapError&lt;DomainError&gt;(StatusCodes.Status400BadRequest);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddTrellisAsp(this IServiceCollection services, Action<TrellisAspOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        // Compose configuration via IConfigureOptions<TrellisAspOptions>: multiple
        // AddTrellisAsp(o => ...) calls (e.g. library + application) all run against
        // the same options instance built lazily by OptionsFactory<TrellisAspOptions>.
        // Resolution remains via GetService<TrellisAspOptions>() through a singleton
        // bridge that materializes from IOptions<TrellisAspOptions>.Value.
        //
        // Replace (not TryAdd): AddTrellisAsp claims the TrellisAspOptions slot. Without
        // Replace, a host's pre-registered TrellisAspOptions instance would silently mask
        // every Configure delegate registered here, so MapError overrides would never
        // reach the resolved options. Hosts customize via the configure action, not via
        // raw AddSingleton(new TrellisAspOptions()).
        services.Configure(configure);
        services.Replace(ServiceDescriptor.Singleton<TrellisAspOptions>(sp =>
            sp.GetRequiredService<IOptions<TrellisAspOptions>>().Value));

        services.TryAddSingleton<ResourceCollectionNameRegistry>();

        // Scalar-value validation is NOT auto-registered. The previous behavior silently
        // mutated MvcOptions and JsonOptions, which surprised consumers who had already
        // configured their own converters / naming policies. Call AddScalarValueValidation()
        // explicitly, or use AddTrellisAspWithScalarValidation() for the all-in-one default.
        return services;
    }

    /// <summary>
    /// Convenience helper that composes <see cref="AddTrellisAsp(IServiceCollection)"/> with
    /// <see cref="AddScalarValueValidation(IServiceCollection)"/>. Use this on greenfield
    /// projects that want the full Trellis ASP.NET Core surface (error mapping + scalar-value
    /// model binding + JSON converters) in a single call.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Equivalent to calling <see cref="AddTrellisAsp(IServiceCollection)"/> followed by
    /// <see cref="AddScalarValueValidation(IServiceCollection)"/>. Scalar-value validation
    /// mutates global <c>MvcOptions</c> and <c>JsonOptions</c>; System.Text.Json resolves
    /// converters in registration order with first-match-wins semantics, so if your host
    /// must override Trellis's `MaybeScalarValueJsonConverterFactory` / `MaybePrimitiveJsonConverterFactory`
    /// / `ValidatingJsonConverterFactory` for some types, register your custom converters
    /// (or your <c>TypeInfoResolver</c>) <em>before</em> calling this helper — or drop down
    /// to <see cref="AddTrellisAsp(IServiceCollection)"/> + explicit
    /// <see cref="AddScalarValueValidation(IServiceCollection)"/> so the ordering is visible
    /// at the call site. Naming-policy / general <c>JsonSerializerOptions</c> tweaks that
    /// do not collide with Trellis converters can register before or after.
    /// </remarks>
    public static IServiceCollection AddTrellisAspWithScalarValidation(this IServiceCollection services)
        => services.AddTrellisAspWithScalarValidation(_ => { });

    /// <summary>
    /// Convenience helper that composes <see cref="AddTrellisAsp(IServiceCollection, Action{TrellisAspOptions})"/>
    /// with <see cref="AddScalarValueValidation(IServiceCollection)"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure error-to-HTTP-status-code mappings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTrellisAspWithScalarValidation(this IServiceCollection services, Action<TrellisAspOptions> configure)
    {
        services.AddTrellisAsp(configure);
        services.AddScalarValueValidation();
        return services;
    }

    /// <summary>
    /// Registers the canonical Trellis ProblemDetails enrichment so unhandled exceptions and
    /// ASP.NET status-code short-circuits (404 / 405 / 415 / 5xx) become RFC 9457 ProblemDetails
    /// responses with a trace id, a support-friendly 500 detail message, and a structured
    /// <c>allow</c> array on 405.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Pair with <see cref="ApplicationBuilderExtensions.UseTrellisProblemDetails(IApplicationBuilder)"/>
    /// in the request pipeline. Without the matching <c>Use*</c> call, ASP.NET routing short-circuits
    /// (404 / 405 / 415) reach the client as bare status responses with no body.
    /// </para>
    /// <para>
    /// <b>Composition.</b> This method ensures <see cref="IProblemDetailsService"/> is registered
    /// (idempotent, safe to call alongside or in lieu of <c>services.AddProblemDetails()</c>).
    /// It then registers a <see cref="IPostConfigureOptions{TOptions}"/> for
    /// <see cref="ProblemDetailsOptions"/> that wraps any consumer-supplied
    /// <see cref="ProblemDetailsOptions.CustomizeProblemDetails"/> delegate. <b>Trellis defaults
    /// run first; the consumer's customization runs last</b>, so consumers can override Trellis
    /// behavior (e.g. tweak the trace-id format, replace the 500 detail message) by setting
    /// <c>CustomizeProblemDetails</c> via <c>services.AddProblemDetails(o => o.CustomizeProblemDetails = ...)</c>
    /// either before or after the call to <c>AddTrellisProblemDetails</c>.
    /// </para>
    /// <para>
    /// <b>Idempotence.</b> Repeated calls are no-ops past the first; Trellis defaults are
    /// applied exactly once per pipeline invocation regardless of how many times this method
    /// runs.
    /// </para>
    /// <para>
    /// <b>What gets enriched.</b>
    /// <list type="bullet">
    /// <item><c>Extensions["traceId"]</c> from <c>Activity.Current?.Id</c>, falling back to
    /// <c>HttpContext.TraceIdentifier</c>.</item>
    /// <item><see cref="ProblemDetails.Detail"/> on 500 responses is replaced with
    /// <c>"An error occurred in our API. Please share the trace id with our support team."</c>
    /// to avoid leaking raw exception detail.</item>
    /// <item><c>Extensions["allow"]</c> on 405 responses contains the methods listed in the
    /// outgoing <c>Allow</c> header (per RFC 9110 §15.5.6) as a structured string array.</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddTrellisProblemDetails();
    ///
    /// var app = builder.Build();
    /// app.UseTrellisProblemDetails();
    /// </code>
    /// </example>
    public static IServiceCollection AddTrellisProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails();

        // Idempotent: register the PostConfigure wrapper exactly once even if
        // AddTrellisProblemDetails is called multiple times (e.g. from both a
        // shared library and the application composition root).
        if (services.Any(d => d.ServiceType == typeof(TrellisProblemDetailsMarker)))
            return services;
        services.AddSingleton<TrellisProblemDetailsMarker>();

        // PostConfigure runs AFTER all Configure callbacks for ProblemDetailsOptions.
        // We capture whatever CustomizeProblemDetails the consumer set (via any prior or
        // posterior AddProblemDetails(...) call — last Configure wins on that single
        // delegate property) and wrap it so Trellis defaults run FIRST, then the
        // consumer's customization runs LAST. Consumers can therefore override any
        // Trellis default by setting their own CustomizeProblemDetails.
        services.PostConfigure<ProblemDetailsOptions>(options =>
        {
            var consumer = options.CustomizeProblemDetails;
            options.CustomizeProblemDetails = ctx =>
            {
                ApplyTrellisProblemDetailsDefaults(ctx);
                consumer?.Invoke(ctx);
            };
        });

        return services;
    }

    private sealed class TrellisProblemDetailsMarker
    {
    }

    private static void ApplyTrellisProblemDetailsDefaults(ProblemDetailsContext ctx)
    {
        // Surface the active trace id so clients can correlate the failure with
        // server-side spans / log entries. Falls back to the connection-level
        // identifier when no diagnostic Activity is current.
        ctx.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;

        // Replace the raw exception detail on 500 with a support-friendly message
        // so internal information (stack-frame paths, message text) does not leak
        // to the client. Application-specific messaging can override this by
        // setting CustomizeProblemDetails after AddTrellisProblemDetails.
        if (ctx.ProblemDetails.Status == StatusCodes.Status500InternalServerError)
        {
            ctx.ProblemDetails.Detail =
                "An error occurred in our API. Please share the trace id with our support team.";
        }

        // RFC 9110 §15.5.6: 405 responses list permitted methods in the Allow header.
        // Echo that into the body as a structured array for clients that ignore the
        // representation header. Tolerates both "GET,HEAD" and "GET, HEAD" forms.
        if (ctx.ProblemDetails.Status == StatusCodes.Status405MethodNotAllowed &&
            ctx.HttpContext.Response.Headers.TryGetValue("Allow", out var allow))
        {
            ctx.ProblemDetails.Extensions["allow"] = allow
                .ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}