namespace Trellis.EntityFrameworkCore;

using System.Reflection;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods for <see cref="ModelConfigurationBuilder"/> that register
/// Trellis value object conventions before EF Core model building.
/// </summary>
public static class ModelConfigurationBuilderExtensions
{
    /// <summary>
    /// Registers pre-convention value converters for all Trellis value object types
    /// found in the specified assemblies plus the built-in Trellis.Primitives assembly.
    /// <para>
    /// This tells EF Core to treat Trellis value objects as scalar properties
    /// <b>before</b> convention processing runs, eliminating the need for inline
    /// <c>HasConversion()</c> calls in <c>OnModelCreating</c>.
    /// </para>
    /// <example>
    /// <code>
    /// protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    /// {
    ///     // Scans your assembly for CustomerId, OrderStatus, etc.
    ///     // Also auto-scans Trellis.Primitives for EmailAddress, Url, PhoneNumber, etc.
    ///     configurationBuilder.ApplyTrellisConventions(typeof(CustomerId).Assembly);
    /// }
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="configurationBuilder">The model configuration builder.</param>
    /// <param name="assemblies">
    /// Assemblies containing Trellis value object types to register.
    /// The Trellis.Primitives assembly (containing <c>EmailAddress</c>, <c>Url</c>, etc.)
    /// is always included automatically.
    /// </param>
    /// <returns>The same <see cref="ModelConfigurationBuilder"/> for chaining.</returns>
    public static ModelConfigurationBuilder ApplyTrellisConventions(
        this ModelConfigurationBuilder configurationBuilder,
        params Assembly[] assemblies)
    {
        var primitivesAssembly = typeof(RequiredEnum<>).Assembly;
        var allAssemblies = new HashSet<Assembly>(assemblies) { primitivesAssembly };

        foreach (var assembly in allAssemblies)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsAbstract)
                    continue;

                var valueObject = TrellisTypeScanner.FindValueObject(type);
                if (valueObject is null)
                    continue;

                var converterType = typeof(TrellisScalarConverter<,>)
                    .MakeGenericType(type, valueObject.Value.ProviderType);
                configurationBuilder.Properties(type).HaveConversion(converterType);
            }
        }

        configurationBuilder.Conventions.Add(static _ => new MaybeConvention());
        configurationBuilder.Conventions.Add(static _ => new MoneyConvention());
        configurationBuilder.Conventions.Add(static _ => new AggregateETagConvention());

        return configurationBuilder;
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null).ToArray()!;
        }
    }
}