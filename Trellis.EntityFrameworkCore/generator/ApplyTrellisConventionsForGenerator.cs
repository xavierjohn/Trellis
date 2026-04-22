namespace Trellis.EntityFrameworkCore.Generator;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Source generator that emits a compile-time, reflection-free implementation of
/// <c>ModelConfigurationBuilder.ApplyTrellisConventionsFor&lt;TContext&gt;()</c>.
/// </summary>
/// <remarks>
/// <para>
/// For every concrete <c>DbContext</c>-derived type in the consuming compilation, the
/// generator walks the <c>DbSet&lt;T&gt;</c> properties, recursively discovers reachable
/// Trellis value object types (scalars and composites) on entity properties, and emits
/// explicit registrations that call <c>AddTrellisScalarConverter&lt;,&gt;</c> and
/// <c>AddTrellisCoreConventions(...)</c> directly for the discovered types.
/// </para>
/// <para>
/// The runtime API uses reflection-based assembly scanning; the generated path is a
/// drop-in replacement for AOT/trim scenarios where reflection over arbitrary
/// assemblies is undesirable. See ADR-002 §8.1.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class ApplyTrellisConventionsForGenerator : IIncrementalGenerator
{
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string ScalarValueObjectMetadataName = "Trellis.ScalarValueObject`2";
    private const string ScalarValueInterfaceMetadataName = "Trellis.IScalarValue`2";
    private const string RequiredEnumMetadataName = "Trellis.RequiredEnum`1";
    private const string ValueObjectMetadataName = "Trellis.ValueObject";
    private const string MaybeMetadataName = "Trellis.Maybe`1";

    // Custom display format: like FullyQualifiedFormat but without UseSpecialTypes, so that
    // the generator never emits keyword aliases (e.g. "string" / "int") and instead always
    // emits "global::System.String" / "global::System.Int32". Keeps emitted code stable and
    // consistent with hardcoded values like the RequiredEnum string provider.
    private static readonly SymbolDisplayFormat s_fqnFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contexts = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax cls
                    && cls.BaseList is not null
                    && !cls.Modifiers.Any(m => m.ValueText == "abstract"),
                transform: static (ctx, ct) => GetDbContextInfo(ctx, ct))
            .Where(static info => info is not null);

        context.RegisterSourceOutput(
            contexts.Collect(),
            static (spc, all) => Execute(spc, all));
    }

    private static DbContextConventionsInfo? GetDbContextInfo(
        GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol symbol)
            return null;
        if (symbol.IsAbstract || symbol.IsGenericType)
            return null;
        if (!IsAtLeastInternal(symbol))
            return null;

        var compilation = ctx.SemanticModel.Compilation;
        var dbContextSymbol = compilation.GetTypeByMetadataName(DbContextMetadataName);
        if (dbContextSymbol is null)
            return null;
        if (!InheritsFrom(symbol, dbContextSymbol))
            return null;

        var dbSetSymbol = compilation.GetTypeByMetadataName(DbSetMetadataName);
        if (dbSetSymbol is null)
            return null;

        var scalarBase = compilation.GetTypeByMetadataName(ScalarValueObjectMetadataName);
        var scalarInterface = compilation.GetTypeByMetadataName(ScalarValueInterfaceMetadataName);
        var requiredEnumBase = compilation.GetTypeByMetadataName(RequiredEnumMetadataName);
        var valueObjectBase = compilation.GetTypeByMetadataName(ValueObjectMetadataName);
        var maybeSymbol = compilation.GetTypeByMetadataName(MaybeMetadataName);
        if (valueObjectBase is null)
            return new DbContextConventionsInfo(
                fullyQualifiedName: ToFullyQualifiedName(symbol),
                scalars: ImmutableArray<ScalarRegistration>.Empty,
                composites: ImmutableArray<string>.Empty);

        var entityRoots = new List<INamedTypeSymbol>();
        foreach (var member in EnumerateInstanceProperties(symbol))
        {
            if (member.Type is not INamedTypeSymbol propType)
                continue;
            if (!SymbolEqualityComparer.Default.Equals(propType.OriginalDefinition, dbSetSymbol))
                continue;
            if (propType.TypeArguments.Length != 1)
                continue;
            if (propType.TypeArguments[0] is INamedTypeSymbol entity && IsAtLeastInternal(entity))
                entityRoots.Add(entity);
        }

        var scalarBuilder = ImmutableArray.CreateBuilder<ScalarRegistration>();
        var compositeBuilder = ImmutableArray.CreateBuilder<string>();
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var classified = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in entityRoots)
        {
            WalkType(root, scalarBase, scalarInterface, requiredEnumBase, valueObjectBase, maybeSymbol,
                visited, classified, scalarBuilder, compositeBuilder);
        }

        // Stable ordering for incremental cache stability + deterministic output.
        var scalars = scalarBuilder.Distinct().OrderBy(s => s.ClrTypeFqn, StringComparer.Ordinal).ToImmutableArray();
        var composites = compositeBuilder.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToImmutableArray();

        return new DbContextConventionsInfo(
            fullyQualifiedName: ToFullyQualifiedName(symbol),
            scalars: scalars,
            composites: composites);
    }

    private static void WalkType(
        INamedTypeSymbol type,
        INamedTypeSymbol? scalarBase,
        INamedTypeSymbol? scalarInterface,
        INamedTypeSymbol? requiredEnumBase,
        INamedTypeSymbol valueObjectBase,
        INamedTypeSymbol? maybeSymbol,
        HashSet<INamedTypeSymbol> visited,
        HashSet<string> classified,
        ImmutableArray<ScalarRegistration>.Builder scalars,
        ImmutableArray<string>.Builder composites)
    {
        if (!visited.Add(type))
            return;

        // Classify this type itself (relevant for property types reached through recursion).
        TryClassify(type, scalarBase, scalarInterface, requiredEnumBase, valueObjectBase, classified, scalars, composites);

        // Walk properties of this type (and inherited) to find more reachable VO types.
        foreach (var prop in EnumerateInstanceProperties(type))
        {
            var propType = Unwrap(prop.Type, maybeSymbol);
            if (propType is not INamedTypeSymbol named)
                continue;
            if (IsExcludedFramework(named))
                continue;
            if (!IsAtLeastInternal(named))
                continue;
            WalkType(named, scalarBase, scalarInterface, requiredEnumBase, valueObjectBase, maybeSymbol,
                visited, classified, scalars, composites);
        }
    }

    private static void TryClassify(
        INamedTypeSymbol type,
        INamedTypeSymbol? scalarBase,
        INamedTypeSymbol? scalarInterface,
        INamedTypeSymbol? requiredEnumBase,
        INamedTypeSymbol valueObjectBase,
        HashSet<string> classified,
        ImmutableArray<ScalarRegistration>.Builder scalars,
        ImmutableArray<string>.Builder composites)
    {
        if (type.IsAbstract || type.IsUnboundGenericType)
            return;
        // Skip open generics (type definitions or partially-constructed types with unresolved
        // type parameters). Closed constructed generics like GenericRange<int> are allowed and
        // match the reflection-based scanner's behavior (TrellisTypeScanner.IsCompositeValueObject).
        if (type.IsGenericType && type.TypeArguments.Any(a => a is ITypeParameterSymbol))
            return;
        if (!IsAtLeastInternal(type))
            return;

        var fqn = ToFullyQualifiedName(type);
        if (!classified.Add(fqn))
            return;

        // Walk base chain to find RequiredEnum<TSelf> (always string-backed) or
        // ScalarValueObject<TSelf, TProvider> (typed provider).
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (requiredEnumBase is not null
                && SymbolEqualityComparer.Default.Equals(b.OriginalDefinition, requiredEnumBase))
            {
                scalars.Add(new ScalarRegistration(fqn, "global::System.String"));
                return;
            }

            if (scalarBase is not null
                && SymbolEqualityComparer.Default.Equals(b.OriginalDefinition, scalarBase)
                && b.TypeArguments.Length == 2
                && b.TypeArguments[1] is INamedTypeSymbol providerType)
            {
                scalars.Add(new ScalarRegistration(fqn, ToFullyQualifiedName(providerType)));
                return;
            }
        }

        // Match TrellisTypeScanner.FindValueObject by also recognizing interface-only scalars
        // that implement IScalarValue<TSelf, TPrimitive> without deriving from ScalarValueObject<,>.
        if (scalarInterface is not null)
        {
            foreach (var iface in type.AllInterfaces)
            {
                if (!iface.IsGenericType
                    || iface.TypeArguments.Length != 2
                    || !SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, scalarInterface))
                    continue;
                if (iface.TypeArguments[1] is INamedTypeSymbol ifaceProvider)
                {
                    scalars.Add(new ScalarRegistration(fqn, ToFullyQualifiedName(ifaceProvider)));
                    return;
                }
            }
        }

        // Composite VO: extends ValueObject (transitively) and was not classified as scalar above.
        if (InheritsFrom(type, valueObjectBase))
            composites.Add(fqn);
    }

    private static ITypeSymbol Unwrap(ITypeSymbol type, INamedTypeSymbol? maybeSymbol)
    {
        // Unwrap arrays (T[] -> T)
        if (type is IArrayTypeSymbol array)
            return Unwrap(array.ElementType, maybeSymbol);

        // Unwrap Nullable<T>
        if (type is INamedTypeSymbol nt && nt.IsGenericType
            && nt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && nt.TypeArguments.Length == 1)
        {
            return Unwrap(nt.TypeArguments[0], maybeSymbol);
        }

        // Unwrap Trellis.Maybe<T>
        if (maybeSymbol is not null
            && type is INamedTypeSymbol mt
            && mt.IsGenericType
            && SymbolEqualityComparer.Default.Equals(mt.OriginalDefinition, maybeSymbol)
            && mt.TypeArguments.Length == 1)
        {
            return Unwrap(mt.TypeArguments[0], maybeSymbol);
        }

        // Unwrap common single-type-arg collection navigations so that VOs reachable only
        // through, e.g., List<Order>.Order are still discovered.
        if (type is INamedTypeSymbol ct && ct.IsGenericType
            && ct.TypeArguments.Length == 1
            && IsCollectionLike(ct))
        {
            return Unwrap(ct.TypeArguments[0], maybeSymbol);
        }

        return type;
    }

    private static bool IsCollectionLike(INamedTypeSymbol type)
    {
        var name = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return name switch
        {
            "global::System.Collections.Generic.IEnumerable<T>" => true,
            "global::System.Collections.Generic.ICollection<T>" => true,
            "global::System.Collections.Generic.IReadOnlyCollection<T>" => true,
            "global::System.Collections.Generic.IList<T>" => true,
            "global::System.Collections.Generic.IReadOnlyList<T>" => true,
            "global::System.Collections.Generic.List<T>" => true,
            "global::System.Collections.Generic.HashSet<T>" => true,
            "global::System.Collections.Generic.ISet<T>" => true,
            "global::System.Collections.Generic.IReadOnlySet<T>" => true,
            "global::System.Collections.ObjectModel.Collection<T>" => true,
            "global::System.Collections.ObjectModel.ReadOnlyCollection<T>" => true,
            _ => false,
        };
    }

    private static IEnumerable<IPropertySymbol> EnumerateInstanceProperties(INamedTypeSymbol type)
    {
        var current = type;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol p && !p.IsStatic && !p.IsIndexer)
                    yield return p;
            }

            current = current.BaseType;
        }
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol? baseType)
    {
        if (baseType is null) return false;
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(b, baseType)
                || SymbolEqualityComparer.Default.Equals(b.OriginalDefinition, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAtLeastInternal(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.ContainingType)
        {
            switch (t.DeclaredAccessibility)
            {
                case Accessibility.Public:
                case Accessibility.Internal:
                case Accessibility.ProtectedOrInternal:
                case Accessibility.NotApplicable:
                    continue;
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool IsExcludedFramework(INamedTypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None) return true;
        var ns = type.ContainingNamespace?.ToDisplayString();
        if (string.IsNullOrEmpty(ns)) return false;
        return ns == "System"
            || ns!.StartsWith("System.", StringComparison.Ordinal)
            || ns == "Microsoft"
            || ns.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    private static string ToFullyQualifiedName(ITypeSymbol type) =>
        type.ToDisplayString(s_fqnFormat);

    private static void Execute(SourceProductionContext spc, ImmutableArray<DbContextConventionsInfo?> all)
    {
        var contexts = all.Where(c => c is not null).Select(c => c!).ToImmutableArray();
        if (contexts.Length == 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Trellis.EntityFrameworkCore;");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Source-generated dispatch for <c>ApplyTrellisConventionsFor&lt;TContext&gt;</c>.");
        sb.AppendLine("/// One private apply method is emitted per concrete <c>DbContext</c>-derived type");
        sb.AppendLine("/// discovered in the current compilation.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        sb.AppendLine("public static class GeneratedTrellisConventions");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Registers Trellis value object converters and conventions for the specified");
        sb.AppendLine("    /// <typeparamref name=\"TContext\"/>. Reflection-free, AOT-clean alternative to");
        sb.AppendLine("    /// <see cref=\"ModelConfigurationBuilderExtensions.ApplyTrellisConventions(ModelConfigurationBuilder, System.Reflection.Assembly[])\"/>.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <typeparam name=\"TContext\">A concrete <see cref=\"DbContext\"/>-derived type defined in the current compilation.</typeparam>");
        sb.AppendLine("    /// <param name=\"configurationBuilder\">The model configuration builder.</param>");
        sb.AppendLine("    /// <returns>The same <see cref=\"ModelConfigurationBuilder\"/> for chaining.</returns>");
        sb.AppendLine("    /// <exception cref=\"InvalidOperationException\">Thrown when no body was generated for <typeparamref name=\"TContext\"/>.</exception>");
        sb.AppendLine("    public static ModelConfigurationBuilder ApplyTrellisConventionsFor<TContext>(");
        sb.AppendLine("        this ModelConfigurationBuilder configurationBuilder)");
        sb.AppendLine("        where TContext : DbContext");
        sb.AppendLine("    {");
        sb.AppendLine("        if (configurationBuilder is null) throw new ArgumentNullException(nameof(configurationBuilder));");
        sb.AppendLine("        var t = typeof(TContext);");
        foreach (var ctxInfo in contexts.OrderBy(c => c.FullyQualifiedName, StringComparer.Ordinal))
        {
            var helperName = HelperMethodName(ctxInfo.FullyQualifiedName);
            sb.Append("        if (t == typeof(");
            sb.Append(ctxInfo.FullyQualifiedName);
            sb.Append(")) return ");
            sb.Append(helperName);
            sb.AppendLine("(configurationBuilder);");
        }

        sb.AppendLine("        throw new InvalidOperationException(");
        sb.AppendLine("            \"No source-generated Trellis conventions exist for DbContext type '\" + t.FullName + \"'. \" +");
        sb.AppendLine("            \"Ensure the type derives from Microsoft.EntityFrameworkCore.DbContext, is concrete, and is defined in the project that calls ApplyTrellisConventionsFor<>().\");");
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var ctxInfo in contexts.OrderBy(c => c.FullyQualifiedName, StringComparer.Ordinal))
        {
            EmitContextHelper(sb, ctxInfo);
        }

        sb.AppendLine("}");

        spc.AddSource("GeneratedTrellisConventions.g.cs", sb.ToString());
    }

    private static void EmitContextHelper(StringBuilder sb, DbContextConventionsInfo ctxInfo)
    {
        var helperName = HelperMethodName(ctxInfo.FullyQualifiedName);
        sb.Append("    private static ModelConfigurationBuilder ");
        sb.Append(helperName);
        sb.AppendLine("(ModelConfigurationBuilder b)");
        sb.AppendLine("    {");
        foreach (var s in ctxInfo.Scalars)
        {
            sb.Append("        global::Trellis.EntityFrameworkCore.ModelConfigurationBuilderExtensions.AddTrellisScalarConverter<");
            sb.Append(s.ClrTypeFqn);
            sb.Append(", ");
            sb.Append(s.ProviderTypeFqn);
            sb.AppendLine(">(b);");
        }

        sb.AppendLine("        var composites = new global::System.Type[]");
        sb.AppendLine("        {");
        foreach (var c in ctxInfo.Composites)
        {
            sb.Append("            typeof(");
            sb.Append(c);
            sb.AppendLine("),");
        }

        sb.AppendLine("        };");
        sb.AppendLine("        return global::Trellis.EntityFrameworkCore.ModelConfigurationBuilderExtensions.AddTrellisCoreConventions(b, composites);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static string HelperMethodName(string fullyQualifiedName)
    {
        var sb = new StringBuilder("ApplyFor_");
        foreach (var ch in fullyQualifiedName)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
            else sb.Append('_');
        }

        // Append a stable hash to disambiguate names that mangle to the same identifier
        // (e.g. "N.Outer.Inner" and "N.Outer_Inner" both collapse underscores).
        sb.Append('_');
        sb.Append(StableHash(fullyQualifiedName).ToString("x8", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static uint StableHash(string value)
    {
        // FNV-1a 32-bit over UTF-16 code units. Deterministic across processes/runtimes.
        const uint offset = 2166136261u;
        const uint prime = 16777619u;
        var hash = offset;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash;
    }

    private readonly struct ScalarRegistration : IEquatable<ScalarRegistration>
    {
        public ScalarRegistration(string clrTypeFqn, string providerTypeFqn)
        {
            ClrTypeFqn = clrTypeFqn;
            ProviderTypeFqn = providerTypeFqn;
        }

        public string ClrTypeFqn { get; }
        public string ProviderTypeFqn { get; }

        public bool Equals(ScalarRegistration other) =>
            string.Equals(ClrTypeFqn, other.ClrTypeFqn, StringComparison.Ordinal)
            && string.Equals(ProviderTypeFqn, other.ProviderTypeFqn, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is ScalarRegistration o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = 17;
                h = (h * 31) + StringComparer.Ordinal.GetHashCode(ClrTypeFqn);
                h = (h * 31) + StringComparer.Ordinal.GetHashCode(ProviderTypeFqn);
                return h;
            }
        }
    }

    private sealed class DbContextConventionsInfo : IEquatable<DbContextConventionsInfo>
    {
        public DbContextConventionsInfo(
            string fullyQualifiedName,
            ImmutableArray<ScalarRegistration> scalars,
            ImmutableArray<string> composites)
        {
            FullyQualifiedName = fullyQualifiedName;
            Scalars = scalars;
            Composites = composites;
        }

        public string FullyQualifiedName { get; }
        public ImmutableArray<ScalarRegistration> Scalars { get; }
        public ImmutableArray<string> Composites { get; }

        public bool Equals(DbContextConventionsInfo? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(FullyQualifiedName, other.FullyQualifiedName, StringComparison.Ordinal)
                && Scalars.SequenceEqual(other.Scalars)
                && Composites.SequenceEqual(other.Composites, StringComparer.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as DbContextConventionsInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = StringComparer.Ordinal.GetHashCode(FullyQualifiedName);
                foreach (var s in Scalars) h = (h * 31) + s.GetHashCode();
                foreach (var c in Composites) h = (h * 31) + StringComparer.Ordinal.GetHashCode(c);
                return h;
            }
        }
    }
}
