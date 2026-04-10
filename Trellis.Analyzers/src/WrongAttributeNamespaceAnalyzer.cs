namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Detects when System.ComponentModel.DataAnnotations [StringLength] or [Range] is applied
/// to a type inheriting from a Trellis base class. The Trellis source generator ignores
/// the DataAnnotations versions, so validation constraints will be silently missing.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WrongAttributeNamespaceAnalyzer : DiagnosticAnalyzer
{
    private const string DataAnnotationsNamespace = "System.ComponentModel.DataAnnotations";
    private const string TrellisNamespace = "Trellis";

    private static readonly string[] s_targetAttributeNames =
    [
        "StringLengthAttribute",
        "RangeAttribute",
    ];

    private static readonly string[] s_trellisBaseTypeNames =
    [
        "ScalarValueObject",
        "RequiredString",
        "RequiredInt",
        "RequiredDecimal",
        "RequiredLong",
        "RequiredGuid",
        "RequiredBool",
        "RequiredDateTime",
        "RequiredEnum",
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.WrongAttributeNamespace];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var typeSymbol = (INamedTypeSymbol)context.Symbol;

        if (!InheritsFromTrellisBaseType(typeSymbol))
            return;

        foreach (var attribute in typeSymbol.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;
            if (attrClass is null)
                continue;

            var attrName = attrClass.Name;
            var attrNamespace = attrClass.ContainingNamespace?.ToDisplayString();

            if (attrNamespace != DataAnnotationsNamespace)
                continue;

            foreach (var targetName in s_targetAttributeNames)
            {
                if (attrName == targetName)
                {
                    // Strip "Attribute" suffix for the message
                    var shortName = targetName.EndsWith("Attribute", StringComparison.Ordinal)
                        ? targetName.Substring(0, targetName.Length - "Attribute".Length)
                        : targetName;

                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.WrongAttributeNamespace,
                        attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? typeSymbol.Locations[0],
                        typeSymbol.Name,
                        shortName);

                    context.ReportDiagnostic(diagnostic);
                    break;
                }
            }
        }
    }

    private static bool InheritsFromTrellisBaseType(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol.BaseType;
        while (current is not null)
        {
            if (current.ContainingNamespace?.ToDisplayString() == TrellisNamespace)
            {
                var name = current.Name;
                foreach (var baseName in s_trellisBaseTypeNames)
                {
                    if (name == baseName)
                        return true;
                }
            }

            current = current.BaseType;
        }

        return false;
    }
}