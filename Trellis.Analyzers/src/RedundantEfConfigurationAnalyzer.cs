namespace Trellis.Analyzers;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects manual EF configuration that duplicates Trellis EF conventions.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RedundantEfConfigurationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.RedundantEfConfiguration];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            if (compilationContext.Compilation.GetTypeByMetadataName("Trellis.EntityFrameworkCore.MaybeConvention") is null)
                return;

            var dbContextSymbol = compilationContext.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
            var conventionContexts = new ConcurrentBag<INamedTypeSymbol>();
            var configurationEntityTypes = new ConcurrentBag<ConfigurationEntityType>();
            var explicitConfigurationContexts = new ConcurrentBag<ConfigurationContext>();
            var entityContexts = new ConcurrentBag<EntityContext>();
            var assemblyConfigurationContexts = new ConcurrentBag<AssemblyConfigurationContext>();
            var pendingDiagnostics = new ConcurrentQueue<PendingDiagnostic>();

            compilationContext.RegisterSymbolAction(
                context => AnalyzeNamedType(context, dbContextSymbol, configurationEntityTypes, entityContexts),
                SymbolKind.NamedType);

            compilationContext.RegisterSyntaxNodeAction(
                context =>
                {
                    var invocation = (InvocationExpressionSyntax)context.Node;
                    if (TryGetTrellisConventionsContext(context, invocation, dbContextSymbol, out var conventionContext))
                    {
                        if (conventionContext is not null)
                            conventionContexts.Add(conventionContext);
                    }

                    AnalyzeConfigurationApplication(
                        context,
                        invocation,
                        dbContextSymbol,
                        explicitConfigurationContexts,
                        assemblyConfigurationContexts);
                    AnalyzeInvocation(context, dbContextSymbol, pendingDiagnostics.Enqueue);
                },
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterCompilationEndAction(context =>
            {
                var configuredContexts = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                foreach (var conventionContext in conventionContexts)
                    configuredContexts.Add(conventionContext);

                if (configuredContexts.Count == 0)
                    return;

                var configurationContexts = BuildConfigurationContextMap(
                    configurationEntityTypes,
                    explicitConfigurationContexts,
                    entityContexts,
                    assemblyConfigurationContexts);

                while (pendingDiagnostics.TryDequeue(out var pendingDiagnostic))
                {
                    if (ShouldReport(
                            pendingDiagnostic,
                            configuredContexts,
                            configurationContexts))
                        context.ReportDiagnostic(pendingDiagnostic.Diagnostic);
                }
            });
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol? dbContextSymbol,
        Action<PendingDiagnostic> reportDiagnostic)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            _ => null
        };

        switch (methodName)
        {
            case "HasConversion":
                AnalyzeHasConversion(context, dbContextSymbol, invocation, memberAccess, reportDiagnostic);
                break;
            case "OwnsOne":
            case "Ignore":
                AnalyzeEntityTypeBuilderConfiguration(context, dbContextSymbol, invocation, memberAccess, methodName, reportDiagnostic);
                break;
        }
    }

    private static void AnalyzeHasConversion(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol? dbContextSymbol,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        Action<PendingDiagnostic> reportDiagnostic)
    {
        if (!IsPropertyBuilderMethod(context, invocation))
            return;

        var propertyInvocation = FindPropertyInvocation(context, memberAccess.Expression);
        if (propertyInvocation is null)
            return;

        if (!TryGetConfiguredProperty(context, propertyInvocation, out var property))
            return;

        ReportIfTrellisConventionProperty(context, dbContextSymbol, reportDiagnostic, memberAccess.Name.GetLocation(), "HasConversion", property);
    }

    private static void AnalyzeEntityTypeBuilderConfiguration(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol? dbContextSymbol,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        string methodName,
        Action<PendingDiagnostic> reportDiagnostic)
    {
        if (!IsEntityTypeBuilderMethod(context, invocation, methodName))
            return;

        if (!TryGetConfiguredProperty(context, invocation, out var property))
            return;

        ReportIfTrellisConventionProperty(context, dbContextSymbol, reportDiagnostic, memberAccess.Name.GetLocation(), methodName, property);
    }

    private static bool TryGetConfiguredProperty(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        out IPropertySymbol property)
    {
        property = null!;

        if (invocation.ArgumentList.Arguments.Count == 0)
            return false;

        if (invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda)
            return false;

        var lambdaParameter = LambdaSyntaxHelpers.GetLambdaParameter(lambda);
        if (lambdaParameter is null)
            return false;

        foreach (var memberAccess in lambda.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (!LambdaSyntaxHelpers.IsAccessOnParameter(memberAccess, lambdaParameter))
                continue;

            if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not IPropertySymbol propertySymbol)
                continue;

            property = propertySymbol;
            return true;
        }

        return false;
    }

    private static void ReportIfTrellisConventionProperty(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol? dbContextSymbol,
        Action<PendingDiagnostic> reportDiagnostic,
        Location location,
        string methodName,
        IPropertySymbol property)
    {
        if (!property.Type.IsMaybeType() && !HasOwnedEntityAttribute(property.Type))
            return;

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.RedundantEfConfiguration,
            location,
            methodName,
            $"{property.ContainingType.Name}.{property.Name}");

        reportDiagnostic(new PendingDiagnostic(
            diagnostic,
            FindEnclosingDbContext(context, dbContextSymbol),
            FindEnclosingEntityTypeConfiguration(context)));
    }

    private static bool IsEntityTypeBuilderMethod(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string expectedMethodName)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol)
            return false;

        if (methodSymbol.Name != expectedMethodName)
            return false;

        return IsEntityTypeBuilder(methodSymbol.ContainingType);
    }

    private static bool IsPropertyBuilderMethod(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol)
            return false;

        var containingType = methodSymbol.ContainingType;
        return containingType?.Name.IndexOf("PropertyBuilder", StringComparison.Ordinal) >= 0 &&
               containingType.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore.Metadata.Builders";
    }

    private static InvocationExpressionSyntax? FindPropertyInvocation(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax expression)
    {
        if (expression is InvocationExpressionSyntax invocation &&
            IsEntityTypeBuilderMethod(context, invocation, "Property"))
            return invocation;

        foreach (var descendantInvocation in expression.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (IsEntityTypeBuilderMethod(context, descendantInvocation, "Property"))
                return descendantInvocation;
        }

        return null;
    }

    private static bool IsEntityTypeBuilder(INamedTypeSymbol? type)
    {
        while (type is not null)
        {
            if (type.Name == "EntityTypeBuilder" &&
                type.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore.Metadata.Builders")
                return true;

            type = type.BaseType;
        }

        return false;
    }

    private static bool HasOwnedEntityAttribute(ITypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is
                {
                    Name: "OwnedEntityAttribute",
                    ContainingNamespace: var ns
                } && ns?.ToDisplayString() == "Trellis.EntityFrameworkCore")
                return true;
        }

        return false;
    }

    private static bool ShouldReport(
        PendingDiagnostic pendingDiagnostic,
        HashSet<INamedTypeSymbol> configuredContexts,
        Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>> configurationContexts)
    {
        if (pendingDiagnostic.EnclosingDbContext is not null)
            return configuredContexts.Contains(pendingDiagnostic.EnclosingDbContext);

        if (pendingDiagnostic.EnclosingConfigurationType is null)
            return false;

        if (!configurationContexts.TryGetValue(pendingDiagnostic.EnclosingConfigurationType, out var attributedContexts) ||
            attributedContexts.Count == 0)
            return false;

        foreach (var attributedContext in attributedContexts)
        {
            if (!configuredContexts.Contains(attributedContext))
                return false;
        }

        return true;
    }

    private static INamedTypeSymbol? FindEnclosingDbContext(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol? dbContextSymbol)
    {
        foreach (var typeDeclaration in context.Node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>())
        {
            if (context.SemanticModel.GetDeclaredSymbol(typeDeclaration, context.CancellationToken) is not INamedTypeSymbol typeSymbol)
                continue;

            if (IsDbContextLike(typeSymbol, dbContextSymbol))
                return typeSymbol;
        }

        return null;
    }

    private static INamedTypeSymbol? FindEnclosingEntityTypeConfiguration(SyntaxNodeAnalysisContext context)
    {
        foreach (var typeDeclaration in context.Node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>())
        {
            if (context.SemanticModel.GetDeclaredSymbol(typeDeclaration, context.CancellationToken) is not INamedTypeSymbol typeSymbol)
                continue;

            if (TryGetEntityTypeConfigurationEntity(typeSymbol, out _))
                return typeSymbol;
        }

        return null;
    }

    private static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        INamedTypeSymbol? dbContextSymbol,
        ConcurrentBag<ConfigurationEntityType> configurationEntityTypes,
        ConcurrentBag<EntityContext> entityContexts)
    {
        var typeSymbol = (INamedTypeSymbol)context.Symbol;
        if (TryGetEntityTypeConfigurationEntity(typeSymbol, out var configuredEntityType))
            configurationEntityTypes.Add(new ConfigurationEntityType(typeSymbol, configuredEntityType));

        if (!IsDbContextLike(typeSymbol, dbContextSymbol))
            return;

        foreach (var property in typeSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (TryGetDbSetEntityType(property.Type, out var entityType))
                entityContexts.Add(new EntityContext(entityType, typeSymbol));
        }
    }

    private static void AnalyzeConfigurationApplication(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol? dbContextSymbol,
        ConcurrentBag<ConfigurationContext> explicitConfigurationContexts,
        ConcurrentBag<AssemblyConfigurationContext> assemblyConfigurationContexts)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        if (!IsModelBuilderMethod(method))
            return;

        var dbContext = FindEnclosingDbContext(context, dbContextSymbol);
        if (dbContext is null)
            return;

        switch (method.Name)
        {
            case "ApplyConfiguration":
                if (TryGetAppliedConfigurationType(context, invocation, out var configurationType))
                    explicitConfigurationContexts.Add(new ConfigurationContext(configurationType, dbContext));
                break;

            case "ApplyConfigurationsFromAssembly":
                if (TryGetAppliedConfigurationsAssembly(context, invocation, out var assembly))
                    assemblyConfigurationContexts.Add(new AssemblyConfigurationContext(assembly, dbContext));
                break;
        }
    }

    private static Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>> BuildConfigurationContextMap(
        IEnumerable<ConfigurationEntityType> configurationEntityTypes,
        IEnumerable<ConfigurationContext> explicitConfigurationContexts,
        IEnumerable<EntityContext> entityContexts,
        IEnumerable<AssemblyConfigurationContext> assemblyConfigurationContexts)
    {
        var map = new Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        var configurationEntities = configurationEntityTypes.ToArray();
        var entityContextMap = new Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>>(SymbolEqualityComparer.Default);

        foreach (var entityContext in entityContexts)
            AddContext(entityContextMap, entityContext.EntityType, entityContext.DbContext);

        foreach (var configurationEntity in configurationEntities)
        {
            if (entityContextMap.TryGetValue(configurationEntity.EntityType, out var contexts))
            {
                foreach (var dbContext in contexts)
                    AddContext(map, configurationEntity.ConfigurationType, dbContext);
            }
        }

        foreach (var configurationContext in explicitConfigurationContexts)
            AddContext(map, configurationContext.ConfigurationType, configurationContext.DbContext);

        foreach (var assemblyContext in assemblyConfigurationContexts)
        {
            foreach (var configurationEntity in configurationEntities)
            {
                if (SymbolEqualityComparer.Default.Equals(
                        configurationEntity.ConfigurationType.ContainingAssembly,
                        assemblyContext.Assembly))
                    AddContext(map, configurationEntity.ConfigurationType, assemblyContext.DbContext);
            }
        }

        return map;
    }

    private static void AddContext(
        Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>> map,
        INamedTypeSymbol key,
        INamedTypeSymbol dbContext)
    {
        if (!map.TryGetValue(key, out var contexts))
        {
            contexts = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            map.Add(key, contexts);
        }

        contexts.Add(dbContext);
    }

    private static bool TryGetAppliedConfigurationType(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        out INamedTypeSymbol configurationType)
    {
        configurationType = null!;

        if (invocation.ArgumentList.Arguments.Count == 0)
            return false;

        if (context.SemanticModel.GetTypeInfo(
                invocation.ArgumentList.Arguments[0].Expression,
                context.CancellationToken).Type is not INamedTypeSymbol type)
            return false;

        if (!TryGetEntityTypeConfigurationEntity(type, out _))
            return false;

        configurationType = type;
        return true;
    }

    private static bool TryGetAppliedConfigurationsAssembly(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        out IAssemblySymbol assembly)
    {
        assembly = null!;

        if (invocation.ArgumentList.Arguments.Count == 0)
            return false;

        var expression = invocation.ArgumentList.Arguments[0].Expression;
        if (expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "Assembly",
                Expression: TypeOfExpressionSyntax typeOfExpression
            } &&
            context.SemanticModel.GetTypeInfo(typeOfExpression.Type, context.CancellationToken).Type is { } type)
        {
            assembly = type.ContainingAssembly;
            return true;
        }

        return false;
    }

    private static bool TryGetEntityTypeConfigurationEntity(INamedTypeSymbol type, out INamedTypeSymbol entityType)
    {
        entityType = null!;

        foreach (var @interface in type.AllInterfaces)
        {
            if (@interface.Name != "IEntityTypeConfiguration" ||
                @interface.TypeArguments.Length != 1)
                continue;

            var @namespace = @interface.ContainingNamespace?.ToDisplayString();
            if (@namespace is not "Microsoft.EntityFrameworkCore" and
                not "Microsoft.EntityFrameworkCore.Metadata.Builders")
                continue;

            if (@interface.TypeArguments[0] is not INamedTypeSymbol namedEntityType)
                continue;

            entityType = namedEntityType;
            return true;
        }

        return false;
    }

    private static bool TryGetDbSetEntityType(ITypeSymbol type, out INamedTypeSymbol entityType)
    {
        entityType = null!;

        if (type is not INamedTypeSymbol { TypeArguments.Length: 1 } namedType)
            return false;

        if (namedType.Name != "DbSet" ||
            namedType.ContainingNamespace?.ToDisplayString() != "Microsoft.EntityFrameworkCore")
            return false;

        if (namedType.TypeArguments[0] is not INamedTypeSymbol namedEntityType)
            return false;

        entityType = namedEntityType;
        return true;
    }

    private static bool IsModelBuilderMethod(IMethodSymbol method) =>
        method.ContainingType is
        {
            Name: "ModelBuilder",
            ContainingNamespace: var ns
        } && ns?.ToDisplayString() == "Microsoft.EntityFrameworkCore";

    private static bool TryGetTrellisConventionsContext(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol? dbContextSymbol,
        out INamedTypeSymbol? dbContext)
    {
        dbContext = null;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return false;

        var originalMethod = method.ReducedFrom ?? method;
        var containingType = originalMethod.ContainingType;
        if (containingType?.ContainingNamespace?.ToDisplayString() != "Trellis.EntityFrameworkCore")
            return false;

        switch (originalMethod.Name)
        {
            case "ApplyTrellisConventions"
                when containingType.Name == "ModelConfigurationBuilderExtensions":
                dbContext = FindEnclosingDbContext(context, dbContextSymbol);
                return true;

            case "ApplyTrellisConventionsFor"
                when containingType.Name is "ModelConfigurationBuilderExtensions" or "GeneratedTrellisConventions":
                dbContext = method.TypeArguments.FirstOrDefault() as INamedTypeSymbol
                    ?? FindEnclosingDbContext(context, dbContextSymbol);
                return true;

            default:
                return false;
        }
    }

    private static bool IsDbContextLike(INamedTypeSymbol typeSymbol, INamedTypeSymbol? dbContextSymbol)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            if (dbContextSymbol is not null && SymbolEqualityComparer.Default.Equals(current, dbContextSymbol))
                return true;

            if (current.Name == "DbContext" &&
                current.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore")
                return true;
        }

        return typeSymbol.Name.EndsWith("DbContext", StringComparison.Ordinal);
    }

    private sealed class PendingDiagnostic
    {
        public PendingDiagnostic(
            Diagnostic diagnostic,
            INamedTypeSymbol? enclosingDbContext,
            INamedTypeSymbol? enclosingConfigurationType)
        {
            Diagnostic = diagnostic;
            EnclosingDbContext = enclosingDbContext;
            EnclosingConfigurationType = enclosingConfigurationType;
        }

        public Diagnostic Diagnostic { get; }

        public INamedTypeSymbol? EnclosingDbContext { get; }

        public INamedTypeSymbol? EnclosingConfigurationType { get; }
    }

    private sealed class ConfigurationEntityType
    {
        public ConfigurationEntityType(INamedTypeSymbol configurationType, INamedTypeSymbol entityType)
        {
            ConfigurationType = configurationType;
            EntityType = entityType;
        }

        public INamedTypeSymbol ConfigurationType { get; }

        public INamedTypeSymbol EntityType { get; }
    }

    private sealed class ConfigurationContext
    {
        public ConfigurationContext(INamedTypeSymbol configurationType, INamedTypeSymbol dbContext)
        {
            ConfigurationType = configurationType;
            DbContext = dbContext;
        }

        public INamedTypeSymbol ConfigurationType { get; }

        public INamedTypeSymbol DbContext { get; }
    }

    private sealed class EntityContext
    {
        public EntityContext(INamedTypeSymbol entityType, INamedTypeSymbol dbContext)
        {
            EntityType = entityType;
            DbContext = dbContext;
        }

        public INamedTypeSymbol EntityType { get; }

        public INamedTypeSymbol DbContext { get; }
    }

    private sealed class AssemblyConfigurationContext
    {
        public AssemblyConfigurationContext(IAssemblySymbol assembly, INamedTypeSymbol dbContext)
        {
            Assembly = assembly;
            DbContext = dbContext;
        }

        public IAssemblySymbol Assembly { get; }

        public INamedTypeSymbol DbContext { get; }
    }
}