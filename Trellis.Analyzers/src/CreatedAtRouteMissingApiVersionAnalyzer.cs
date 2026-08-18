namespace Trellis.Analyzers;

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// TRLS023: Warns when <c>HttpResponseOptionsBuilder&lt;T&gt;.CreatedAtRoute(...)</c>,
/// <c>HttpResponseOptionsBuilder&lt;T&gt;.CreatedAtAction(...)</c>, or
/// <c>HttpResponseOptionsBuilder&lt;T&gt;.WithLocation(...)</c> is invoked on a versioned
/// controller (one with <c>[ApiVersion(...)]</c>) without either (a) an <c>"api-version"</c>
/// entry in the supplied route-values dictionary literal, or (b) a chained
/// <c>.WithVersionedRoute(...)</c> call. The resulting <c>Location</c> header omits the
/// version under query/header API versioning and 404s on dereference. The fix chains
/// <c>.WithVersionedRoute()</c> from <c>Trellis.Asp.ApiVersioning</c>, which injects the
/// version per-request.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CreatedAtRouteMissingApiVersionAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.MissingApiVersionRouteValue);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var inv = (InvocationExpressionSyntax)context.Node;

        // Quick syntactic filter: must look like a Location-emitting builder method.
        if (inv.Expression is not MemberAccessExpressionSyntax mae) return;
        var methodName = mae.Name.Identifier.Text;
        if (methodName is not ("CreatedAtRoute" or "CreatedAtAction" or "WithLocation")) return;

        // Resolve the symbol to confirm it's HttpResponseOptionsBuilder<T> on Trellis.Asp.
        var methodSymbol = context.SemanticModel.GetSymbolInfo(inv).Symbol as IMethodSymbol;
        if (methodSymbol is null) return;
        var containingType = methodSymbol.ContainingType;
        if (containingType is null) return;
        if (containingType.Name != "HttpResponseOptionsBuilder") return;
        if (containingType.ContainingNamespace?.ToDisplayString() != "Trellis.Asp") return;

        // Only fire inside a class that's api-versioned (has [ApiVersion]) and not [ApiVersionNeutral].
        // [ApiVersionNeutral] is also valid on individual actions (AttributeTargets.Class | Method),
        // so we additionally check the containing method symbol — a single [ApiVersionNeutral] action
        // on an otherwise-versioned controller must not trigger TRLS023.
        var classDecl = inv.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDecl is null) return;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl);
        if (classSymbol is null) return;
        if (!HasAttribute(classSymbol, "ApiVersionAttribute", "Asp.Versioning")) return;
        if (HasAttribute(classSymbol, "ApiVersionNeutralAttribute", "Asp.Versioning")) return;

        var methodDecl = inv.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (methodDecl is not null &&
            context.SemanticModel.GetDeclaredSymbol(methodDecl) is IMethodSymbol containingMethodSymbol &&
            HasAttributeOnMethod(containingMethodSymbol, "ApiVersionNeutralAttribute", "Asp.Versioning"))
        {
            return;
        }

        // Skip if the same fluent builder chain already contains `.WithVersionedRoute(...)`.
        // The api-version is supplied per-request — no need to encode it in the route-values literal.
        if (ChainSuppressesApiVersionWarning(inv, context.SemanticModel))
            return;

        // Otherwise, inspect the route-values argument. If it's a literal that already includes
        // an "api-version" key, the chain is also correct (author opted out of WithVersionedRoute
        // and supplied the value by hand). For non-literal forms we bail to false-negative.
        if (inv.ArgumentList.Arguments.Count >= 2)
        {
            var routeValuesArg = inv.ArgumentList.Arguments[1];
            var isSingleIdOverload = IsSingleIdOverload(methodSymbol);
            var shape = ClassifyRouteValuesShape(routeValuesArg, isSingleIdOverload);
            switch (shape.Kind)
            {
                case RouteValuesShapeKind.Unrecognized:
                    return;

                case RouteValuesShapeKind.Initializer:
                    if (RouteValueDictionaryContainsApiVersionKey(shape.Initializer!, context.SemanticModel))
                        return;
                    break;

                case RouteValuesShapeKind.AnonymousObjectCtorArg:
                    // C# property names cannot contain hyphens, so an anonymous-object route-values
                    // ctor argument can never carry an "api-version" property. Fall through.
                    break;

                case RouteValuesShapeKind.SingleIdSelector:
                    // The (string, Func<T, object>, string idRouteKey = "id") overload — the
                    // id route key is always a non-"api-version" identifier (default "id"), and
                    // the framework constructs the dictionary internally with that single entry.
                    // Without a chained WithVersionedRoute, the api-version is necessarily missing.
                    break;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.MissingApiVersionRouteValue,
            inv.GetLocation(),
            methodName));
    }

    /// <summary>
    /// Walks the surrounding fluent chain (both directions) and returns true when any link
    /// would inject the <c>api-version</c> route value at runtime. Two recognised forms:
    /// <list type="bullet">
    ///   <item><description><c>.WithVersionedRoute(...)</c> from <c>Trellis.Asp.ApiVersioning</c> (sugar).</description></item>
    ///   <item><description><c>.WithRouteValueResolver("api-version", ...)</c> on a
    ///     <c>Trellis.Asp.HttpResponseOptionsBuilder&lt;T&gt;</c> — the manual primitive that
    ///     <c>WithVersionedRoute</c> itself wraps.</description></item>
    /// </list>
    /// Walks both downstream (calls chained AFTER <paramref name="inv"/>) and upstream (calls that
    /// appear earlier in the chain on the same receiver). Order doesn't matter at runtime —
    /// both forms just register a route-value resolver — so we accept both.
    /// </summary>
    /// <remarks>
    /// Resolves each candidate to its method symbol and verifies the containing type/namespace,
    /// so a user-defined extension with the same simple name on an unrelated type cannot
    /// falsely suppress this diagnostic. The outer-shape check earlier in
    /// <see cref="AnalyzeInvocation"/> uses the same semantic-model gate for symmetry.
    /// </remarks>
    private static bool ChainSuppressesApiVersionWarning(InvocationExpressionSyntax inv, SemanticModel semanticModel)
    {
        SyntaxNode? current = inv;
        while (current?.Parent is MemberAccessExpressionSyntax outerMae &&
               outerMae.Expression == current &&
               outerMae.Parent is InvocationExpressionSyntax outerInv)
        {
            if (IsSuppressingInvocation(outerInv, semanticModel))
                return true;
            current = outerInv;
        }

        ExpressionSyntax? receiver = (inv.Expression as MemberAccessExpressionSyntax)?.Expression;
        while (receiver is InvocationExpressionSyntax receiverInv &&
               receiverInv.Expression is MemberAccessExpressionSyntax receiverMae)
        {
            if (IsSuppressingInvocation(receiverInv, semanticModel))
                return true;
            receiver = receiverMae.Expression;
        }

        return false;
    }

    private static bool IsSuppressingInvocation(InvocationExpressionSyntax candidate, SemanticModel semanticModel) =>
        IsTrellisWithVersionedRoute(candidate, semanticModel) ||
        IsTrellisWithRouteValueResolverForApiVersion(candidate, semanticModel);

    /// <summary>
    /// Returns true when <paramref name="candidate"/> is a call to
    /// <c>Trellis.Asp.ApiVersioning.HttpResponseOptionsBuilderApiVersioningExtensions.WithVersionedRoute</c>.
    /// Cheap name-match first to short-circuit the common case, then semantic verification.
    /// </summary>
    private static bool IsTrellisWithVersionedRoute(InvocationExpressionSyntax candidate, SemanticModel semanticModel)
    {
        if (candidate.Expression is not MemberAccessExpressionSyntax mae)
            return false;
        if (mae.Name.Identifier.Text != "WithVersionedRoute")
            return false;

        if (semanticModel.GetSymbolInfo(candidate).Symbol is not IMethodSymbol method)
            return false;

        // Extension methods resolve to the reduced symbol (instance form); ReducedFrom carries
        // the original static extension method whose containing type holds the namespace we care
        // about. Fall back to ContainingType when the symbol isn't a reduced extension.
        var containingType = method.ReducedFrom?.ContainingType ?? method.ContainingType;
        if (containingType is null) return false;
        return containingType.Name == "HttpResponseOptionsBuilderApiVersioningExtensions" &&
               containingType.ContainingNamespace?.ToDisplayString() == "Trellis.Asp.ApiVersioning";
    }

    /// <summary>
    /// Returns true when <paramref name="candidate"/> is a call to
    /// <c>Trellis.Asp.HttpResponseOptionsBuilder&lt;T&gt;.WithRouteValueResolver</c> whose first
    /// argument is a compile-time-constant string equal to <c>"api-version"</c>
    /// (case-insensitive — RouteValueDictionary keys are case-insensitive at runtime).
    /// </summary>
    /// <remarks>
    /// This is the manual primitive that <c>WithVersionedRoute()</c> wraps internally —
    /// suppressing TRLS023 here keeps the analyzer honest about the chain's runtime behavior
    /// even when the author skipped the sugar. Restricting the match to the Trellis builder
    /// type prevents a same-named extension on an unrelated type from silently suppressing the
    /// diagnostic.
    /// </remarks>
    private static bool IsTrellisWithRouteValueResolverForApiVersion(InvocationExpressionSyntax candidate, SemanticModel semanticModel)
    {
        if (candidate.Expression is not MemberAccessExpressionSyntax mae)
            return false;
        if (mae.Name.Identifier.Text != "WithRouteValueResolver")
            return false;
        if (candidate.ArgumentList.Arguments.Count == 0)
            return false;

        var key = TryGetConstantStringValue(candidate.ArgumentList.Arguments[0].Expression, semanticModel);
        if (key is null || !string.Equals(key, "api-version", StringComparison.OrdinalIgnoreCase))
            return false;

        if (semanticModel.GetSymbolInfo(candidate).Symbol is not IMethodSymbol method)
            return false;

        // WithRouteValueResolver is an instance method on HttpResponseOptionsBuilder<T>, not an
        // extension — no ReducedFrom unwrap needed. Gate on the Trellis.Asp namespace so a
        // same-named method on an unrelated type can't accidentally suppress the diagnostic.
        var containingType = method.ContainingType;
        if (containingType is null) return false;
        return containingType.Name == "HttpResponseOptionsBuilder" &&
               containingType.ContainingNamespace?.ToDisplayString() == "Trellis.Asp";
    }

    private enum RouteValuesShapeKind
    {
        Unrecognized,
        Initializer,
        AnonymousObjectCtorArg,
        SingleIdSelector,
    }

    private readonly struct RouteValuesShape
    {
        public RouteValuesShape(RouteValuesShapeKind kind, InitializerExpressionSyntax? initializer)
        {
            Kind = kind;
            Initializer = initializer;
        }

        public RouteValuesShapeKind Kind { get; }
        public InitializerExpressionSyntax? Initializer { get; }
    }

    private static bool HasAttribute(INamedTypeSymbol type, string attributeTypeName, string attributeNamespace)
    {
        // [ApiVersion] / [ApiVersionNeutral] are declared with Inherited = false, so look only at
        // the immediate type. Walking the base-type chain would treat derived controllers without
        // their own [ApiVersion] as versioned, which would produce a false positive that API
        // Versioning itself doesn't accept.
        foreach (var attr in type.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac?.Name == attributeTypeName &&
                ac.ContainingNamespace?.ToDisplayString() == attributeNamespace)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAttributeOnMethod(IMethodSymbol method, string attributeTypeName, string attributeNamespace)
    {
        foreach (var attr in method.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac?.Name == attributeTypeName &&
                ac.ContainingNamespace?.ToDisplayString() == attributeNamespace)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSingleIdOverload(IMethodSymbol method)
    {
        if (method.Name is not ("CreatedAtRoute" or "WithLocation"))
            return false;
        if (method.Parameters.Length < 3)
            return false;
        if (method.Parameters[1].Type is not INamedTypeSymbol selectorType)
            return false;
        if (selectorType.Name != "Func" || selectorType.TypeArguments.Length != 2)
            return false;

        return selectorType.TypeArguments[1].SpecialType == SpecialType.System_Object;
    }

    private static RouteValuesShape ClassifyRouteValuesShape(ArgumentSyntax arg, bool isSingleIdOverload)
    {
        // Lambda body shapes we recognize:
        //   c => new RouteValueDictionary { ... }            → Initializer
        //   c => { return new RouteValueDictionary { ... }; }→ Initializer
        //   c => new RouteValueDictionary(new { ... })       → AnonymousObjectCtorArg
        //
        // The single-id overloads `CreatedAtRoute(name, idSelector, idRouteKey = "id")` and
        // `WithLocation(name, idSelector, idRouteKey = "id")` pass a `Func<T, object>` in arg 1;
        // when we detect that overload via the method symbol (3-parameter form), the api-version
        // is necessarily missing unless `.WithVersionedRoute()` is chained — even for non-lambda
        // selectors (method group, delegate variable). For the 2-arg dict overload, a non-literal
        // lambda body is Unrecognized (computed dictionary — we can't tell what's inside).
        if (arg.Expression is not LambdaExpressionSyntax lambda)
        {
            return isSingleIdOverload
                ? new RouteValuesShape(RouteValuesShapeKind.SingleIdSelector, null)
                : new RouteValuesShape(RouteValuesShapeKind.Unrecognized, null);
        }

        ExpressionSyntax? bodyExpr = lambda.Body switch
        {
            ExpressionSyntax e => e,
            BlockSyntax block => block.Statements
                .OfType<ReturnStatementSyntax>()
                .Select(r => r.Expression!)
                .FirstOrDefault(),
            _ => null,
        };

        if (bodyExpr is BaseObjectCreationExpressionSyntax oce)
        {
            if (oce.Initializer is { } init)
                return new RouteValuesShape(RouteValuesShapeKind.Initializer, init);

            // No initializer block — look for an anonymous-object constructor argument.
            // Anonymous-object property names are valid C# identifiers (no hyphens), so this
            // shape definitionally cannot carry an "api-version" key.
            if (oce.ArgumentList?.Arguments.Count == 1 &&
                oce.ArgumentList.Arguments[0].Expression is AnonymousObjectCreationExpressionSyntax)
            {
                return new RouteValuesShape(RouteValuesShapeKind.AnonymousObjectCtorArg, null);
            }

            return new RouteValuesShape(RouteValuesShapeKind.Unrecognized, null);
        }

        // Non-dictionary lambda body. For the single-id overload the body returns the id
        // expression directly; the framework constructs the dictionary internally. For the
        // 2-arg dict overload, the body must produce a RouteValueDictionary — we can't tell
        // what a computed dictionary contains, so treat as Unrecognized.
        return isSingleIdOverload
            ? new RouteValuesShape(RouteValuesShapeKind.SingleIdSelector, null)
            : new RouteValuesShape(RouteValuesShapeKind.Unrecognized, null);
    }

    private static bool RouteValueDictionaryContainsApiVersionKey(
        InitializerExpressionSyntax init,
        SemanticModel semanticModel)
    {
        // RouteValueDictionary uses case-insensitive key comparison at runtime. Match the same
        // semantics here so that {"API-VERSION"], ["Api-Version"], etc. are accepted as equivalent
        // to "api-version" — otherwise the analyzer fires on a literal that's already correct.
        // Const-string identifiers are also accepted (e.g., `[ApiVersionKey] = ...` where
        // `ApiVersionKey` is `const string ApiVersionKey = "api-version";`) — the semantic model
        // resolves the constant value.
        foreach (var expr in init.Expressions)
        {
            // `["api-version"] = x` — collection-initializer assignment with bracket key.
            if (expr is AssignmentExpressionSyntax ae &&
                ae.Left is ImplicitElementAccessSyntax iea)
            {
                var key = TryGetStringKey(iea.ArgumentList.Arguments, semanticModel);
                if (key is not null && string.Equals(key, "api-version", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            // `{ "api-version", x }` — pre-C#6 collection-initializer-style dictionary entry.
            else if (expr is InitializerExpressionSyntax pair &&
                     pair.Expressions.FirstOrDefault() is { } first &&
                     TryGetConstantStringValue(first, semanticModel) is { } pairKey &&
                     string.Equals(pairKey, "api-version", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryGetStringKey(
        SeparatedSyntaxList<ArgumentSyntax> args,
        SemanticModel semanticModel)
    {
        if (args.Count == 0) return null;
        return TryGetConstantStringValue(args[0].Expression, semanticModel);
    }

    private static string? TryGetConstantStringValue(ExpressionSyntax expr, SemanticModel semanticModel)
    {
        // Direct string literal: cheap path, no semantic-model query.
        if (expr is LiteralExpressionSyntax lit && lit.Token.IsKind(SyntaxKind.StringLiteralToken))
            return lit.Token.ValueText;

        // Reference to a const string symbol (IdentifierNameSyntax / MemberAccessExpressionSyntax)
        // — let the semantic model resolve the constant value at the call site.
        var constant = semanticModel.GetConstantValue(expr);
        if (constant.HasValue && constant.Value is string s)
            return s;

        return null;
    }
}