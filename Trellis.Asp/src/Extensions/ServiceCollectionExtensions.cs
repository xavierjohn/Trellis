namespace Trellis.Asp;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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
    ///     public IActionResult Register(RegisterUserDto dto)
    ///     {
    ///         // If we reach here, dto is fully validated!
    ///         // All scalar values passed validation
    ///
    ///         var user = User.TryCreate(dto.Email, dto.FirstName);
    ///         return user.ToActionResult(this);
    ///     }
    /// }
    /// </code>
    /// </example>
    public static IMvcBuilder AddScalarValueValidation(this IMvcBuilder builder)
    {
        builder.Services.Configure<MvcJsonOptions>(options =>
            ConfigureJsonOptions(options.JsonSerializerOptions));

        builder.Services.Configure<MvcOptions>(options =>
            options.Filters.Add<ScalarValueValidationFilter>());

        builder.AddMvcOptions(options =>
        {
            options.ModelBinderProviders.Insert(0, new ScalarValueModelBinderProvider());

            // Prevent MVC's ValidationVisitor from recursing into Maybe<T> properties.
            // Without this, accessing Maybe<T>.Value via reflection throws when HasNoValue.
            options.ModelMetadataDetailsProviders.Add(
                new MaybeSuppressChildValidationMetadataProvider());
        });

        // Configure [ApiController] to not automatically return 400 for invalid ModelState
        // This allows our ScalarValueValidationFilter to handle validation errors properly
        builder.Services.Configure<ApiBehaviorOptions>(options =>
            options.SuppressModelStateInvalidFilter = true);

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
    private static void ConfigureJsonOptions(JsonSerializerOptions options)
    {
        // Use TypeInfoResolver modifier to assign converters per-property with property name
#pragma warning disable IL2026, IL3050 // DefaultJsonTypeInfoResolver requires dynamic code - this is fallback path
        var existingResolver = options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
#pragma warning restore IL2026, IL3050
        options.TypeInfoResolver = existingResolver.WithAddedModifier(ModifyTypeInfo);

        // Add factories for direct serialization scenarios
        options.Converters.Add(new ValidatingJsonConverterFactory());
        options.Converters.Add(new MaybeScalarValueJsonConverterFactory());
    }

    /// <summary>
    /// Modifies type info to inject property names into ValidationErrorsContext before deserialization.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "PropertyType comes from JSON serialization infrastructure which preserves type information")]
    private static void ModifyTypeInfo(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

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

            // Check if it's a direct value object (IScalarValue<TSelf, T>)
            if (!IsScalarValueProperty(property))
                continue;

            // Create a validating converter for this value object
            var innerScalarConverter = CreateValidatingConverter(propertyType);
            if (innerScalarConverter is null)
                continue;

            // Wrap it with property name awareness
            var wrappedScalarConverter = CreatePropertyNameAwareConverter(innerScalarConverter, property.Name, propertyType);
            if (wrappedScalarConverter is not null)
                property.CustomConverter = wrappedScalarConverter;
        }
    }

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

#pragma warning disable IL2055, IL3050 // MakeGenericType and Activator require dynamic code
    private static JsonConverter? CreatePropertyNameAwareConverter(JsonConverter innerConverter, string propertyName, Type type)
    {
        var wrapperType = typeof(PropertyNameAwareConverter<>).MakeGenericType(type);
        return Activator.CreateInstance(wrapperType, innerConverter, propertyName) as JsonConverter;
    }
#pragma warning restore IL2055, IL3050

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
    /// <strong>Note:</strong> This method does NOT configure MVC-specific features like model binding
    /// or the validation filter. If you're using MVC controllers with the <c>[ApiController]</c> attribute,
    /// use <c>AddControllers().AddScalarValueValidation()</c> instead for full functionality.
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
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddTrellisAsp();
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
        var options = new TrellisAspOptions();
        configure(options);
        services.AddSingleton(options);
        TrellisAspOptions.Default = options;
        return services;
    }
}