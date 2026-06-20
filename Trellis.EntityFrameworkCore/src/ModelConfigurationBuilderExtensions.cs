namespace Trellis.EntityFrameworkCore;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Trellis.Authorization;
using Trellis.Primitives;

/// <summary>
/// Extension methods for <see cref="ModelConfigurationBuilder"/> that register
/// Trellis value object conventions before EF Core model building.
/// </summary>
public static class ModelConfigurationBuilderExtensions
{
    /// <summary>
    /// Registers pre-convention value converters for all Trellis value object types
    /// found in the specified assemblies plus the built-in Trellis.Core, Trellis.Primitives,
    /// and Trellis.Authorization assemblies.
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
    ///     // Trellis.Core (base Required* types), Trellis.Primitives (EmailAddress, Url,
    ///     // PhoneNumber, ...) and Trellis.Authorization (ActorId) are included in the default
    ///     // scan set, so persisted audit fields like Order.CreatedByActorId : ActorId get
    ///     // their scalar converter without an explicit typeof(ActorId).Assembly hand-in.
    ///     configurationBuilder.ApplyTrellisConventions(typeof(CustomerId).Assembly);
    /// }
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="configurationBuilder">The model configuration builder.</param>
    /// <param name="assemblies">
    /// Assemblies containing Trellis value object types to register.
    /// The Trellis.Core assembly (base <c>Required*</c> types), the Trellis.Primitives assembly
    /// (containing <c>EmailAddress</c>, <c>Url</c>, etc.), and the Trellis.Authorization assembly
    /// (containing <c>ActorId</c>) are always included automatically.
    /// </param>
    /// <returns>The same <see cref="ModelConfigurationBuilder"/> for chaining.</returns>
    public static ModelConfigurationBuilder ApplyTrellisConventions(
        this ModelConfigurationBuilder configurationBuilder,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ArgumentNullException.ThrowIfNull(assemblies);
        for (var i = 0; i < assemblies.Length; i++)
        {
            if (assemblies[i] is null)
                throw new ArgumentException($"Assembly at index [{i}] is null.", nameof(assemblies));
        }

        var coreAssembly = typeof(RequiredEnum<>).Assembly;
        var primitivesAssembly = typeof(EmailAddress).Assembly;
        var authorizationAssembly = typeof(ActorId).Assembly;
        var allAssemblies = new HashSet<Assembly>(assemblies)
        {
            coreAssembly,
            primitivesAssembly,
            authorizationAssembly,
        };
        var scalars = new List<(Type ClrType, Type ProviderType)>();
        var composites = new HashSet<Type>();

        foreach (var assembly in allAssemblies)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsAbstract)
                    continue;

                var valueObject = TrellisTypeScanner.FindValueObject(type);
                if (valueObject is not null)
                    scalars.Add((type, valueObject.Value.ProviderType));
                else if (TrellisTypeScanner.IsCompositeValueObject(type))
                    composites.Add(type);
            }
        }

        return configurationBuilder.ApplyTrellisConventionsCore(scalars, composites);
    }

    /// <summary>
    /// Low-level convention-registration helper used by both the reflection-based
    /// <see cref="ApplyTrellisConventions(ModelConfigurationBuilder, Assembly[])"/> overload
    /// and the source-generated <c>ApplyTrellisConventionsFor&lt;TContext&gt;</c> entry point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most users should call <see cref="ApplyTrellisConventions(ModelConfigurationBuilder, Assembly[])"/>
    /// or the generated <c>ApplyTrellisConventionsFor&lt;TContext&gt;</c> extension instead. This method
    /// is exposed for source generators that have already classified Trellis value object types at
    /// compile time.
    /// </para>
    /// <para>
    /// Registers a <see cref="TrellisScalarConverter{TSelf, T}"/> for each scalar entry, then adds the
    /// fixed Trellis conventions (<c>MaybeConvention</c>, <c>CompositeValueObjectConvention</c>,
    /// <c>MoneyConvention</c>, <c>AggregateETagConvention</c>, <c>AggregateTransientPropertyConvention</c>,
    /// <c>ValueObjectMappingGuardConvention</c>).
    /// </para>
    /// </remarks>
    /// <param name="configurationBuilder">The model configuration builder.</param>
    /// <param name="scalars">
    /// Scalar Trellis value object types, each paired with the EF Core provider primitive type that
    /// the converter should round-trip to (e.g. <c>(typeof(CustomerId), typeof(Guid))</c>).
    /// </param>
    /// <param name="composites">
    /// Composite (non-scalar) Trellis <c>ValueObject</c> types to register with
    /// <c>CompositeValueObjectConvention</c>.
    /// </param>
    /// <returns>The same <see cref="ModelConfigurationBuilder"/> for chaining.</returns>
    public static ModelConfigurationBuilder ApplyTrellisConventionsCore(
        this ModelConfigurationBuilder configurationBuilder,
        IEnumerable<(Type ClrType, Type ProviderType)> scalars,
        IEnumerable<Type> composites)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ArgumentNullException.ThrowIfNull(scalars);
        ArgumentNullException.ThrowIfNull(composites);

        foreach (var (clrType, providerType) in scalars)
        {
            ArgumentNullException.ThrowIfNull(clrType, nameof(clrType));
            ArgumentNullException.ThrowIfNull(providerType, nameof(providerType));
            var converterType = typeof(TrellisScalarConverter<,>).MakeGenericType(clrType, providerType);
            configurationBuilder.Properties(clrType).HaveConversion(converterType);
        }

        return configurationBuilder.AddTrellisCoreConventions(composites);
    }

    /// <summary>
    /// Registers a strongly-typed <see cref="TrellisScalarConverter{TSelf, T}"/> for
    /// <typeparamref name="TClr"/> properties that round-trip to <typeparamref name="TProvider"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reflection-free: no calls to <see cref="Type.MakeGenericType(Type[])"/> or other
    /// runtime reflection. Intended for source-generated convention registration.
    /// </para>
    /// <para>
    /// This registers only the value converter. The Trellis conventions — including
    /// <see cref="ScalarValueObjectPropertyConvention"/>, which maps constructor-bound (get-only)
    /// Trellis value-object properties (scalar and symbolic enum) — are installed by <see cref="AddTrellisCoreConventions"/>
    /// (which <see cref="ApplyTrellisConventions(ModelConfigurationBuilder, System.Reflection.Assembly[])"/>
    /// and the generated <c>ApplyTrellisConventionsFor&lt;TContext&gt;</c> both call). A complete setup
    /// must call one of those in addition to any standalone <c>AddTrellisScalarConverter</c> calls.
    /// </para>
    /// </remarks>
    /// <typeparam name="TClr">The Trellis scalar value object CLR type (e.g. <c>CustomerId</c>).</typeparam>
    /// <typeparam name="TProvider">The provider primitive type (e.g. <c>Guid</c>, <c>string</c>).</typeparam>
    /// <param name="configurationBuilder">The model configuration builder.</param>
    /// <returns>The same <see cref="ModelConfigurationBuilder"/> for chaining.</returns>
    public static ModelConfigurationBuilder AddTrellisScalarConverter<TClr, TProvider>(
        this ModelConfigurationBuilder configurationBuilder)
        where TClr : class
        where TProvider : notnull
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<TClr>().HaveConversion<TrellisScalarConverter<TClr, TProvider>>();
        return configurationBuilder;
    }

    /// <summary>
    /// Adds the fixed Trellis EF Core conventions: <c>MaybeConvention</c>,
    /// <c>CompositeValueObjectConvention</c>, <c>MoneyConvention</c>, <c>AggregateETagConvention</c>,
    /// <c>AggregateTransientPropertyConvention</c>, and <c>ValueObjectMappingGuardConvention</c>.
    /// </summary>
    /// <remarks>
    /// Reflection-free: no calls to <see cref="Type.MakeGenericType(Type[])"/>. The
    /// <paramref name="composites"/> argument carries already-closed <see cref="Type"/> tokens
    /// supplied by the caller (typically the source generator).
    /// </remarks>
    /// <param name="configurationBuilder">The model configuration builder.</param>
    /// <param name="composites">Composite Trellis <c>ValueObject</c> types.</param>
    /// <returns>The same <see cref="ModelConfigurationBuilder"/> for chaining.</returns>
    public static ModelConfigurationBuilder AddTrellisCoreConventions(
        this ModelConfigurationBuilder configurationBuilder,
        IEnumerable<Type> composites)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ArgumentNullException.ThrowIfNull(composites);

        var compositeSet = composites as HashSet<Type> ?? new HashSet<Type>(composites);
        configurationBuilder.Conventions.Add(static _ => new ScalarValueObjectPropertyConvention());
        configurationBuilder.Conventions.Add(static _ => new MaybeConvention());
        configurationBuilder.Conventions.Add(_ => new CompositeValueObjectConvention(compositeSet));
        configurationBuilder.Conventions.Add(static _ => new MoneyConvention());
        configurationBuilder.Conventions.Add(static _ => new AggregateETagConvention());
        configurationBuilder.Conventions.Add(static _ => new AggregateTransientPropertyConvention());
        configurationBuilder.Conventions.Add(static _ => new ValueObjectMappingGuardConvention());

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