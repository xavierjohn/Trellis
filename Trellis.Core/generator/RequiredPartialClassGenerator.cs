namespace SourceGenerator;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Trellis.PrimitiveValueObjectGenerator;

/// <summary>
/// C# source generator that automatically creates factory methods, validation logic, and parsing support
/// for value objects inheriting from <c>RequiredGuid</c>, <c>RequiredString</c>, <c>RequiredInt</c>,
/// <c>RequiredLong</c>, <c>RequiredDecimal</c>, <c>RequiredBool</c>, <c>RequiredDateTime</c>, or <c>RequiredEnum</c>.
/// </summary>
/// <remarks>
/// <para>
/// This incremental source generator analyzes partial class declarations and generates complementary code
/// that provides a complete, production-ready value object implementation. It eliminates boilerplate while
/// maintaining type safety and validation consistency.
/// </para>
/// <para>
/// For each supported base type, the generator creates:
/// <list type="bullet">
/// <item><c>IScalarValue&lt;TSelf, TPrimitive&gt;</c> — ASP.NET Core model binding and JSON deserialization</item>
/// <item><c>TryCreate</c> overloads — non-nullable, nullable, and string with validation</item>
/// <item><c>Create</c> — throwing factory for known-valid values</item>
/// <item><c>ValidateAdditional</c> — optional partial method hook for custom validation</item>
/// <item><c>IParsable&lt;T&gt;</c> — <c>Parse</c> and <c>TryParse</c></item>
/// <item>Private constructor, explicit cast operator, JSON converter attribute</item>
/// <item>OpenTelemetry activity tracing</item>
/// </list>
/// </para>
/// <para>
/// Type-specific behavior:
/// <list type="bullet">
/// <item><c>RequiredGuid</c> — rejects <c>Guid.Empty</c>; generates <c>NewUniqueV4()</c>, <c>NewUniqueV7()</c>, and <c>NewUniqueV7(TimeProvider)</c></item>
/// <item><c>RequiredString</c> — rejects null/empty/whitespace; trims; supports <c>[StringLength]</c></item>
/// <item><c>RequiredInt</c> — supports <c>[Range(int, int)]</c></item>
/// <item><c>RequiredLong</c> — supports <c>[Range(long, long)]</c></item>
/// <item><c>RequiredDecimal</c> — supports <c>[Range(int, int)]</c> and <c>[Range(double, double)]</c></item>
/// <item><c>RequiredBool</c> — accepts true/false; rejects null</item>
/// <item><c>RequiredDateTime</c> — rejects <c>DateTime.MinValue</c>; ISO 8601 round-trip <c>ToString</c></item>
/// <item><c>RequiredEnum</c> — smart enum; delegates to <c>TryFromName</c></item>
/// </list>
/// </para>
/// <para>
/// Benefits of using the source generator:
/// <list type="bullet">
/// <item><strong>Zero boilerplate</strong>: Write just the class declaration, get full implementation</item>
/// <item><strong>Consistency</strong>: All value objects have identical validation and factory patterns</item>
/// <item><strong>Type safety</strong>: Prevents primitive obsession with strongly-typed wrappers</item>
/// <item><strong>Performance</strong>: Generated code is compiled, no runtime reflection</item>
/// <item><strong>Observability</strong>: Built-in OpenTelemetry tracing for all operations</item>
/// <item><strong>Serialization</strong>: Automatic JSON support for APIs and persistence</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Minimal value object definition:
/// <code>
/// // Your code - just the declaration
/// public partial class CustomerId : RequiredGuid
/// {
/// }
/// 
/// public partial class FirstName : RequiredString
/// {
/// }
/// 
/// public partial class OrderState : RequiredEnum&lt;OrderState&gt;
/// {
///     public static readonly OrderState Draft = new();
///     public static readonly OrderState Confirmed = new();
///     [EnumValue("awaiting-payment")]
///     public static readonly OrderState AwaitingPayment = new();
/// }
/// 
/// // Generated code provides everything else:
/// var id = CustomerId.NewUniqueV7();
/// var nameResult = FirstName.TryCreate("John");
/// </code>
/// </example>
/// <example>
/// Generated code for RequiredGuid (CustomerId):
/// <code>
/// // &lt;auto-generated/&gt;
/// namespace MyApp.Domain;
/// using Trellis;
/// using System.Diagnostics.CodeAnalysis;
/// using System.Text.Json.Serialization;
///
/// [JsonConverter(typeof(ParsableJsonConverter&lt;CustomerId&gt;)]
/// public partial class CustomerId : IScalarValue&lt;CustomerId, Guid&gt;, IParsable&lt;CustomerId&gt;
/// {
///     private CustomerId(Guid value) : base(value) { }
///
///     public static explicit operator CustomerId(Guid customerId)
///         =&gt; Create(customerId);
///
///     public static CustomerId NewUniqueV4() =&gt; new(Guid.NewGuid());
///     public static CustomerId NewUniqueV7() =&gt; new(Guid.CreateVersion7());
///     public static CustomerId NewUniqueV7(TimeProvider timeProvider)
///     {
///         ArgumentNullException.ThrowIfNull(timeProvider);
///         return new(Guid.CreateVersion7(timeProvider.GetUtcNow()));
///     }
///
///     // Required by IScalarValue - enables automatic ASP.NET Core validation
///     public static Result&lt;CustomerId&gt; TryCreate(Guid value)
///         =&gt; TryCreate((Guid?)value, null);
///
///     public static Result&lt;CustomerId&gt; TryCreate(Guid? requiredGuidOrNothing, string? fieldName = null)
///     {
///         using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity("CustomerId.TryCreate");
///         var field = fieldName.NormalizeFieldName("customerId");
///         return requiredGuidOrNothing
///             .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = "Customer Id cannot be empty." }})))
///             .Ensure(x =&gt; x != Guid.Empty, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = "Customer Id cannot be empty." }})))
///             .Map(guid =&gt; new CustomerId(guid));
///     }
///
///     public static Result&lt;CustomerId&gt; TryCreate(string? stringOrNull, string? fieldName = null)
///     {
///         // Parsing logic with validation...
///     }
///
///     public static CustomerId Parse(string s, IFormatProvider? provider) { /* ... */ }
///     public static bool TryParse(...) { /* ... */ }
/// }
/// </code>
/// </example>
[Generator(LanguageNames.CSharp)]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class RequiredPartialClassGenerator : IIncrementalGenerator
{
    private const string HelpLinkBase = "https://xavierjohn.github.io/Trellis/api_reference/trellis-api-analyzers.html";

    /// <summary>
    /// Canonical IDs for diagnostics emitted by this generator. See
    /// <c>Trellis.TrellisDiagnosticIds</c> in <c>Trellis.Analyzers</c> for the
    /// consumer-facing equivalents.
    /// </summary>
    private static class Ids
    {
        public const string UnsupportedRequiredBaseType = "TRLS031";
        public const string InvalidStringLengthRange = "TRLS032";
        public const string InvalidRangeMinExceedsMax = "TRLS033";
        public const string DecimalRangeExceedsDecimalRange = "TRLS034";
        // TRLS040 / TRLS041 / TRLS042 retired in the v3 defaults flip — see TRLS046 / TRLS047 below.
        public const string NumericConvenienceOnNonNumeric = "TRLS043";
        public const string NumericConvenienceConflict = "TRLS044";
        public const string NumericConvenienceWithExplicitRange = "TRLS045";
        public const string NotDefaultIsVestigial = "TRLS046";
        public const string TrimIsVestigial = "TRLS047";
        public const string AllowZeroOnNonNumericRequired = "TRLS048";
        public const string AllowEmptyOnNumericOrDateRequired = "TRLS049";
        public const string AllowMinValueOnNonDateRequired = "TRLS050";
        public const string AllowWhitespaceOnNonStringRequired = "TRLS051";
        public const string NoTrimOnNonStringRequired = "TRLS052";
        public const string ContradictoryRequiredAttributeCombination = "TRLS053";
        public const string GeneratedMemberCollision = "TRLS056";
    }

    /// <summary>
    /// Initializes the incremental generator, setting up the syntax provider and compilation pipeline.
    /// </summary>
    /// <param name="context">The initialization context provided by the compiler.</param>
    /// <remarks>
    /// <para>
    /// This method configures the incremental generator pipeline:
    /// <list type="number">
    /// <item>Creates a syntax provider that identifies candidate classes (partial classes with base types)</item>
    /// <item>Filters to only classes inheriting from a supported Required* base type</item>
    /// <item>Combines syntax nodes with compilation for semantic analysis</item>
    /// <item>Registers the source output callback to generate code</item>
    /// </list>
    /// </para>
    /// <para>
    /// The incremental approach ensures:
    /// <list type="bullet">
    /// <item>Fast incremental builds - only regenerates changed files</item>
    /// <item>Minimal IDE impact - efficient background generation</item>
    /// <item>Correct dependency tracking - regenerates when base classes change</item>
    /// </list>
    /// </para>
    /// </remarks>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ClassDeclarationSyntax> requiredGuids = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (n, _) => IsSyntaxTargetForGeneration(n),
                transform: static (ctx, _) => (ClassDeclarationSyntax)ctx.Node
                ).Where(m => m is not null);

        IncrementalValueProvider<(Compilation, ImmutableArray<ClassDeclarationSyntax>)> compilationAndEnums
            = context.CompilationProvider.Combine(requiredGuids.Collect());

        context.RegisterSourceOutput(compilationAndEnums,
            static (spc, source) => Execute(source.Item1, source.Item2, spc));
    }

    /// <summary>
    /// Executes the source generation for all identified value object classes.
    /// </summary>
    /// <param name="compilation">The current compilation containing the user's code.</param>
    /// <param name="classes">The class declarations identified as value objects to generate.</param>
    /// <param name="context">The source production context for adding generated source.</param>
    /// <remarks>
    /// <para>
    /// This method:
    /// <list type="number">
    /// <item>Extracts metadata (namespace, class name, base type) from each class</item>
    /// <item>Determines which template to use based on the Required* base class</item>
    /// <item>Generates the complementary partial class source code</item>
    /// <item>Adds the generated source to the compilation</item>
    /// </list>
    /// </para>
    /// <para>
    /// Generated files are named "{ClassName}.g.cs" to clearly indicate they are generated
    /// and should not be manually edited.
    /// </para>
    /// </remarks>
    private static void Execute(Compilation compilation, ImmutableArray<ClassDeclarationSyntax> classes, SourceProductionContext context)
    {
        // I'm not sure if this is actually necessary, but `[LoggerMessage]` does it, so seems like a good idea!
        IEnumerable<ClassDeclarationSyntax> distinctClasses = classes.Distinct();

        List<RequiredPartialClassInfo> classesToGenerate = GetTypesToGenerate(compilation, distinctClasses, context.CancellationToken);

        foreach (var g in classesToGenerate)
        {
            var camelArg = g.ClassName.ToCamelCase();
            var classType = g.ClassBase switch
            {
                "RequiredGuid" => "Guid",
                "RequiredString" => "string",
                "RequiredInt" => "int",
                "RequiredDecimal" => "decimal",
                "RequiredLong" => "long",
                "RequiredBool" => "bool",
                "RequiredDateTime" => "DateTime",
                "RequiredDateTimeOffset" => "DateTimeOffset",
                "RequiredEnum" => "enum",
                _ => null
            };

            // Skip unsupported base types and emit a diagnostic to inform the user
            if (classType is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        id: Ids.UnsupportedRequiredBaseType,
                        title: "Unsupported base type for RequiredPartialClassGenerator",
                        messageFormat: "Class '{0}' inherits from unsupported base type '{1}'. Supported bases: RequiredGuid, RequiredString, RequiredInt, RequiredDecimal, RequiredLong, RequiredBool, RequiredDateTime, RequiredDateTimeOffset, RequiredEnum.",
                        category: "Trellis",
                        DiagnosticSeverity.Warning,
                        isEnabledByDefault: true),
                    location: null,
                    g.ClassName,
                    g.ClassBase));
                continue;
            }

            var nestedTypeOpen = BuildNestingOpen(g);
            var nestedTypeClose = BuildNestingClose(g);

            // Defense in depth: emit a generator diagnostic for invalid attribute combinations
            // even if the user suppresses the matching analyzer rule. Skip generation if invalid.
            if (!ValidateAttributeUsage(g, context))
                continue;

            // RequiredEnum has a completely different structure - handle separately
            if (g.ClassBase == "RequiredEnum")
            {
                var reportedEnumCollisions = new HashSet<string>(StringComparer.Ordinal);
                var enumSource = RemoveConflictingGeneratedMembers(
                    GenerateEnumSource(g, nestedTypeOpen, nestedTypeClose),
                    g,
                    context,
                    reportedEnumCollisions);
                context.AddSource($"{g.TypePath}.g.cs", enumSource);
                continue;
            }

            // Build up the source code
            // Note: The base class is already declared in the user's partial class.
            // We only generate the additional members and interfaces.
            var isFormattable = g.ClassBase is "RequiredInt" or "RequiredDecimal" or "RequiredLong" or "RequiredDateTime" or "RequiredDateTimeOffset";
            var formattableInterface = isFormattable
                ? $", IFormattableScalarValue<{g.ClassName}, {classType}>"
                : "";
            var namespaceDeclaration = NamespaceDeclaration(g);

            var reportedCollisions = new HashSet<string>(StringComparer.Ordinal);
            var sourceBuilder = new StringBuilder($@"// <auto-generated/>
    {namespaceDeclaration}
    using System;
    using Trellis;
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Serialization;

    #nullable enable
            {nestedTypeOpen}
    [JsonConverter(typeof(ParsableJsonConverter<{g.ClassName}>))]
    {g.Accessibility} partial class {g.ClassName} : IScalarValue<{g.ClassName}, {classType}>{formattableInterface}, IParsable<{g.ClassName}>
    {{");

            sourceBuilder.Append(EmitMemberIfNotConflicting(
                g,
                context,
                reportedCollisions,
                ".ctor",
                MemberSignature(".ctor", classType),
                g.ClassName,
                $@"
        private {g.ClassName}({classType} value) : base(value)
        {{
        }}"));

            sourceBuilder.Append(EmitMemberIfNotConflicting(
                g,
                context,
                reportedCollisions,
                "op_Explicit",
                // Signature matches the user-capture and syntax-scan shape:
                // `op_Explicit({source} -> {target})`. An unrelated user explicit operator
                // (e.g. `operator string(MyId)`) has a different signature and is NOT a collision.
                $"op_Explicit({classType} -> {g.ClassName})",
                $"explicit operator {g.ClassName}",
                $@"

        /// <summary>
        /// Explicitly wraps a raw <paramref name=""{camelArg}""/> value into a
        /// validated <see cref=""{g.ClassName}""/>. Equivalent to calling <c>Create</c>.
        /// </summary>
        public static explicit operator {g.ClassName}({classType} {camelArg}) => Create({camelArg});",
                generatedNameOnly: false));

            sourceBuilder.Append(EmitMemberIfNotConflicting(
                g,
                context,
                reportedCollisions,
                "Parse",
                MemberSignature("Parse", "string", "IFormatProvider"),
                "Parse",
                $@"

        /// <summary>
        /// Parses the input into a <see cref=""{g.ClassName}""/> or throws
        /// <see cref=""FormatException""/> when the input fails validation.
        /// </summary>
        /// <param name=""s"">The value to parse.</param>
        /// <param name=""provider"">Format provider (currently unused for non-IFormattable bases).</param>
        public static {g.ClassName} Parse(string s, IFormatProvider? provider)
        {{
            var r = TryCreate(s, {(isFormattable ? "provider" : "null")});
            return r.Match(
                onSuccess: value => value,
                onFailure: error =>
                {{
                    var val = (Error.InvalidInput)error;
                    throw new FormatException(val.Fields.Items[0].Detail ?? val.Fields.Items[0].ReasonCode);
                }});
        }}"));

            sourceBuilder.Append(EmitMemberIfNotConflicting(
                g,
                context,
                reportedCollisions,
                "TryParse",
                MemberSignature("TryParse", "string", "IFormatProvider", OutParameter(g.ClassName)),
                "TryParse",
                $@"

        /// <summary>
        /// Attempts to parse the input into a <see cref=""{g.ClassName}""/>.
        /// Returns <see langword=""false""/> when the input fails validation.
        /// </summary>
        /// <param name=""s"">The value to parse.</param>
        /// <param name=""provider"">Format provider (currently unused for non-IFormattable bases).</param>
        /// <param name=""result"">The parsed value on success; otherwise <see langword=""default""/>.</param>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out {g.ClassName} result)
        {{
            var r = TryCreate(s, {(isFormattable ? "provider" : "null")});
            if (r.TryGetValue(out var value))
            {{
                result = value;
                return true;
            }}

            result = default;
            return false;
        }}"));

            // Generate type-specific factory methods (TryCreate, Create, validation hooks)
            var methods = g.ClassBase switch
            {
                "RequiredGuid" => GenerateGuidMethods(g, context, reportedCollisions),
                "RequiredString" => GenerateStringMethods(g, context, reportedCollisions),
                "RequiredInt" => GenerateIntMethods(g, context, reportedCollisions),
                "RequiredDecimal" => GenerateDecimalMethods(g, context, reportedCollisions),
                "RequiredLong" => GenerateLongMethods(g, context, reportedCollisions),
                "RequiredBool" => GenerateBoolMethods(g, context, reportedCollisions),
                "RequiredDateTime" => GenerateDateTimeMethods(g, context, reportedCollisions),
                "RequiredDateTimeOffset" => GenerateDateTimeOffsetMethods(g, context, reportedCollisions),
                _ => null
            };

            if (methods is null) continue;

            sourceBuilder.Append(methods);
            sourceBuilder.Append($@"
    }}
    {nestedTypeClose}");

            var source = RemoveConflictingGeneratedMembers(sourceBuilder.ToString(), g, context, reportedCollisions);
            context.AddSource($"{g.TypePath}.g.cs", source);
        }
    }

    private static Diagnostic CreateGeneratedMemberCollisionDiagnostic(
        Location? location,
        string displayName,
        string className,
        string classBase) =>
        Diagnostic.Create(
            new DiagnosticDescriptor(
                id: Ids.GeneratedMemberCollision,
                title: "Generated member conflicts with user declaration",
                messageFormat: "Member '{0}' is generated by Trellis for '{1}' (a '{2}' derivation). Remove the redundant declaration, or change the base class if you need custom semantics.",
                category: "Trellis",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: "The required value-object generator found a user member with the same signature as a generated factory, parser, conversion, constructor, or validation hook. " +
                             "The generator suppresses the conflicting member to avoid duplicate-member compiler errors, but the user declaration shadows Trellis' validated surface.",
                helpLinkUri: HelpLinkBase),
            location,
            displayName,
            className,
            classBase);

    private static string EmitMemberIfNotConflicting(
        RequiredPartialClassInfo info,
        SourceProductionContext context,
        HashSet<string> reportedCollisions,
        string name,
        string signature,
        string displayName,
        string source,
        bool generatedNameOnly = false)
    {
        var collision = FindCollision(info, name, signature, generatedNameOnly);
        if (collision is null) return source;

        if (reportedCollisions.Add(collision.ReportKey))
        {
            context.ReportDiagnostic(CreateGeneratedMemberCollisionDiagnostic(
                collision.Location,
                displayName,
                info.ClassName,
                info.ClassBase));
        }

        return string.Empty;
    }

    private static GeneratedMemberDeclaration? FindCollision(
        RequiredPartialClassInfo info,
        string name,
        string signature,
        bool generatedNameOnly) =>
        info.UserDeclaredMembers.FirstOrDefault(member => member.Matches(name, signature, generatedNameOnly));

    private static string RemoveConflictingGeneratedMembers(
        string source,
        RequiredPartialClassInfo info,
        SourceProductionContext context,
        HashSet<string> reportedCollisions)
    {
        if (info.UserDeclaredMembers.Length == 0)
            return source;

        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: context.CancellationToken);
        var root = tree.GetRoot(context.CancellationToken);
        var targetClass = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(candidate => candidate.Identifier.ValueText == info.ClassName);
        if (targetClass is null)
            return source;

        var membersToRemove = new List<MemberDeclarationSyntax>();
        foreach (var member in targetClass.Members)
        {
            if (!TryGetGeneratedMemberIdentity(member, info, out var name, out var signature, out var displayName, out var generatedNameOnly))
                continue;

            var collision = FindCollision(info, name, signature, generatedNameOnly);
            if (collision is null)
                continue;

            if (reportedCollisions.Add(collision.ReportKey))
            {
                context.ReportDiagnostic(CreateGeneratedMemberCollisionDiagnostic(
                    collision.Location,
                    displayName,
                    info.ClassName,
                    info.ClassBase));
            }

            membersToRemove.Add(member);
        }

        if (membersToRemove.Count == 0)
            return source;

        return root.RemoveNodes(membersToRemove, SyntaxRemoveOptions.KeepNoTrivia)?.ToFullString() ?? source;
    }

    private static bool TryGetGeneratedMemberIdentity(
        MemberDeclarationSyntax member,
        RequiredPartialClassInfo info,
        out string name,
        out string signature,
        out string displayName,
        out bool generatedNameOnly)
    {
        switch (member)
        {
            case ConstructorDeclarationSyntax constructor:
                name = ".ctor";
                signature = MemberSignature(name, constructor.ParameterList.Parameters.Select(FormatGeneratedParameterForSignature).ToArray());
                displayName = info.ClassName;
                generatedNameOnly = false;
                return true;
            case ConversionOperatorDeclarationSyntax { ImplicitOrExplicitKeyword.RawKind: (int)SyntaxKind.ExplicitKeyword } convOp:
                name = "op_Explicit";
                // Conversion identity now includes source + target, so an unrelated user explicit
                // operator (e.g. `operator string(CustomerId)`) doesn't collide with the generated
                // `operator CustomerId(Guid)`.
                var sourceType = FormatGeneratedParameterForSignature(convOp.ParameterList.Parameters[0]);
                var targetType = convOp.Type.ToString();
                signature = $"op_Explicit({sourceType} -> {targetType})";
                displayName = $"explicit operator {info.ClassName}";
                generatedNameOnly = false;
                return true;
            case MethodDeclarationSyntax method:
                name = method.Identifier.ValueText;
                signature = MemberSignature(name, method.ParameterList.Parameters.Select(FormatGeneratedParameterForSignature).ToArray());
                displayName = name;
                generatedNameOnly = false;
                return true;
            default:
                name = string.Empty;
                signature = string.Empty;
                displayName = string.Empty;
                generatedNameOnly = false;
                return false;
        }
    }

    private static string FormatGeneratedParameterForSignature(ParameterSyntax parameter)
    {
        var typeName = parameter.Type?.ToString() ?? string.Empty;
        if (typeName is "string?" or "IFormatProvider?")
            typeName = typeName.Substring(0, typeName.Length - 1);

        if (parameter.Modifiers.Any(SyntaxKind.OutKeyword))
            return OutParameter(typeName);
        if (parameter.Modifiers.Any(SyntaxKind.RefKeyword))
            return RefParameter(typeName);
        if (parameter.Modifiers.Any(SyntaxKind.InKeyword))
            return $"in {typeName}";

        return typeName;
    }

    private static string MemberSignature(string name, params string[] parameterTypes) =>
        $"{name}({string.Join(", ", parameterTypes)})";

    private static string OutParameter(string typeName) => $"out {typeName}";

    private static string RefParameter(string typeName) => $"ref {typeName}";
    private static string GenerateEnumSource(RequiredPartialClassInfo g, string nestedTypeOpen, string nestedTypeClose) =>
        $@"// <auto-generated/>
    {NamespaceDeclaration(g)}
    using System;
    using Trellis;
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Serialization;

    #nullable enable
    {nestedTypeOpen}
    [JsonConverter(typeof(RequiredEnumJsonConverter<{g.ClassName}>))]
    {g.Accessibility} partial class {g.ClassName} : IScalarValue<{g.ClassName}, string>, IParsable<{g.ClassName}>
    {{
        /// <summary>
        /// Creates a validated instance from a string by looking up the enum member by symbolic value.
        /// Required by IScalarValue interface for model binding and JSON deserialization.
        /// </summary>
        /// <param name=""value"">The symbolic value to look up.</param>
        /// <returns>Success with the enum member, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(string value)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            return TryFromName(value, null);
        }}

        /// <summary>
        /// Creates a validated instance from a string by looking up the enum member by symbolic value.
        /// </summary>
        /// <param name=""value"">The symbolic value to look up.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the enum member, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(string? value, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            return TryFromName(value, fieldName);
        }}

        /// <summary>
        /// Parses the input into a <see cref=""{g.ClassName}""/> by symbolic name lookup,
        /// or throws <see cref=""FormatException""/> when no member matches.
        /// </summary>
        /// <param name=""s"">The symbolic name to look up.</param>
        /// <param name=""provider"">Format provider (unused for enum derivations).</param>
        public static {g.ClassName} Parse(string s, IFormatProvider? provider)
        {{
            var r = TryCreate(s, null);
            return r.Match(
                onSuccess: value => value,
                onFailure: error =>
                {{
                    var val = (Error.InvalidInput)error;
                    throw new FormatException(val.Fields.Items[0].Detail ?? val.Fields.Items[0].ReasonCode);
                }});
        }}

        /// <summary>
        /// Attempts to parse the input into a <see cref=""{g.ClassName}""/> by symbolic name lookup.
        /// Returns <see langword=""false""/> when no member matches.
        /// </summary>
        /// <param name=""s"">The symbolic name to look up.</param>
        /// <param name=""provider"">Format provider (unused for enum derivations).</param>
        /// <param name=""result"">The matching member on success; otherwise <see langword=""default""/>.</param>
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out {g.ClassName} result)
        {{
            var r = TryCreate(s, null);
            if (r.TryGetValue(out var value))
            {{
                result = value;
                return true;
            }}

            result = default;
            return false;
        }}

        /// <summary>
        /// Creates a validated instance from a string. Throws if validation fails.
        /// Use this for known-valid values in tests or with constants.
        /// </summary>
        /// <param name=""value"">The symbolic value to look up.</param>
        /// <returns>The validated enum member.</returns>
        /// <exception cref=""InvalidOperationException"">Thrown when validation fails.</exception>
        public static {g.ClassName} Create(string value)
        {{
            var result = TryCreate(value, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}
    }}
    {nestedTypeClose}";

    private static string GenerateGuidMethods(RequiredPartialClassInfo g, SourceProductionContext context, HashSet<string> reportedCollisions)
    {
        var emptyDetail = $@"""{g.ClassName.SplitPascalCase()} cannot be Guid.Empty.""";
        var emptyCheck = g.HasAllowEmpty
            ? ""
            : $@"
            if (value == Guid.Empty)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {emptyDetail} }})));";
        var emptyNullableEnsure = g.HasAllowEmpty
            ? ""
            : $@"
                .Ensure(x => x != Guid.Empty, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {emptyDetail} }})))";
        var emptyParsedEnsure = g.HasAllowEmpty
            ? ""
            : $@"
                .Ensure(_ => parsedGuid != Guid.Empty, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {emptyDetail} }})))";

        return $@"

        /// <summary>
        /// Optional validation hook. Implement this partial method to add custom validation.
        /// Called after built-in validations pass.
        /// </summary>
        /// <param name=""value"">The validated Guid value.</param>
        /// <param name=""fieldName"">The normalized field name for error messages.</param>
        /// <param name=""errorMessage"">Set to a non-null string to reject the value.</param>
        static partial void ValidateAdditional(Guid value, string fieldName, ref string? errorMessage);

        /// <summary>
        /// Creates a new instance with a unique Version 4 (random) GUID.
        /// </summary>
        /// <returns>A new instance with a randomly generated GUID.</returns>
        public static {g.ClassName} NewUniqueV4() => new(Guid.NewGuid());

        /// <summary>
        /// Creates a new instance with a unique Version 7 (time-ordered) GUID.
        /// Version 7 GUIDs are time-sortable, making them ideal for database primary keys
        /// as they reduce index fragmentation compared to random Version 4 GUIDs.
        /// </summary>
        /// <returns>A new instance with a time-ordered GUID.</returns>
        /// <remarks>
        /// Requires .NET 9 or later. The generated GUID contains a Unix timestamp
        /// with millisecond precision, followed by random data for uniqueness.
        /// </remarks>
        public static {g.ClassName} NewUniqueV7() => new(Guid.CreateVersion7());

        /// <summary>
        /// Creates a new instance with a unique Version 7 (time-ordered) GUID using the specified time provider.
        /// </summary>
        /// <param name=""timeProvider"">The time provider used to supply the GUID timestamp.</param>
        /// <returns>A new instance with a time-ordered GUID.</returns>
        /// <exception cref=""ArgumentNullException"">Thrown when <paramref name=""timeProvider""/> is <see langword=""null""/>.</exception>
        public static {g.ClassName} NewUniqueV7(TimeProvider timeProvider)
        {{
            ArgumentNullException.ThrowIfNull(timeProvider);
            return new(Guid.CreateVersion7(timeProvider.GetUtcNow()));
        }}

        /// <summary>
        /// Creates a validated instance from a Guid.
        /// Required by IScalarValue interface for model binding and JSON deserialization.
        /// </summary>
        /// <param name=""value"">The Guid value to validate.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(Guid value, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");{emptyCheck}
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(Guid? requiredGuidOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = requiredGuidOrNothing
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }}))){emptyNullableEnsure};
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(guid => new {g.ClassName}(guid));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            Guid parsedGuid = default;
            var validated = stringOrNull
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => Guid.TryParse(x, out parsedGuid), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Guid should contain 32 digits with 4 dashes (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)"" }}))){emptyParsedEnsure};
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedGuid, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(guid => new {g.ClassName}(parsedGuid));
        }}

        /// <summary>
        /// Creates a validated instance from a Guid. Throws if validation fails.
        /// Use this for known-valid values in tests or with constants.
        /// </summary>
        /// <param name=""value"">The Guid value to validate.</param>
        /// <returns>The validated value object.</returns>
        /// <exception cref=""InvalidOperationException"">Thrown when validation fails.</exception>
        public static new {g.ClassName} Create(Guid value)
        {{
            var result = TryCreate(value, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}

        /// <summary>
        /// Creates a validated instance from a string by parsing it as a Guid. Throws if validation or parsing fails.
        /// Use this for known-valid GUID strings in tests or with constants.
        /// </summary>
        /// <param name=""stringValue"">The string value to parse and validate.</param>
        /// <returns>The validated value object.</returns>
        /// <exception cref=""InvalidOperationException"">Thrown when validation or parsing fails.</exception>
        public static {g.ClassName} Create(string stringValue)
        {{
            var result = TryCreate(stringValue, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}";
    }

    private static string NamespaceDeclaration(RequiredPartialClassInfo g) =>
        string.IsNullOrEmpty(g.NameSpace) ? string.Empty : $"namespace {g.NameSpace};";

    /// <summary>
    /// Validates that the <c>[NotDefault]</c> and <c>[Trim]</c> attributes (if present) are
    /// applied to a compatible <c>Required*</c> base. Emits a generator diagnostic when the
    /// combination is invalid. Defense in depth — the same combinations are also flagged by
    /// the matching analyzer rules in <c>Trellis.Analyzers</c>, but the generator must refuse
    /// to emit broken code even if the analyzer is disabled or suppressed.
    /// </summary>
    /// <returns><c>true</c> when generation may proceed; <c>false</c> when generation must be skipped.</returns>
    private static bool ValidateAttributeUsage(RequiredPartialClassInfo g, SourceProductionContext context)
    {
        var ok = true;
        var isNumericBase = g.ClassBase is "RequiredInt" or "RequiredLong" or "RequiredDecimal";
        var isDateBase = g.ClassBase is "RequiredDateTime" or "RequiredDateTimeOffset";
        var numericConvenienceCount = (g.HasPositive ? 1 : 0)
            + (g.HasNonNegative ? 1 : 0)
            + (g.HasNegative ? 1 : 0)
            + (g.HasNonPositive ? 1 : 0);

        if (g.HasNotDefault)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.NotDefaultIsVestigial,
                    title: "[NotDefault] is vestigial",
                    messageFormat: "Class '{0}' has [NotDefault]. Attribute is now vestigial under the v3 strict-by-default model. Remove it; the strict behavior it opted into is now the default. To opt OUT use [AllowEmpty]/[AllowZero]/[AllowMinValue] as appropriate.",
                    category: "Trellis",
                    DiagnosticSeverity.Info,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName));
        }

        if (g.HasTrim)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.TrimIsVestigial,
                    title: "[Trim] is vestigial",
                    messageFormat: "Class '{0}' has [Trim]. Attribute is now vestigial under the v3 strict-by-default model. On RequiredString trim now runs by default — remove this attribute and use [NoTrim] to opt out. On other Required bases [Trim] has never had meaning and is silently ignored.",
                    category: "Trellis",
                    DiagnosticSeverity.Info,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName));
        }

        if (g.HasAllowZero && !isNumericBase)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.AllowZeroOnNonNumericRequired,
                    title: "[AllowZero] is only valid on numeric Required bases",
                    messageFormat: "Class '{0}' has [AllowZero] but inherits from '{1}'. [AllowZero] only applies to RequiredInt, RequiredLong, and RequiredDecimal.",
                    category: "Trellis",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName,
                g.ClassBase));
            ok = false;
        }

        if (g.HasAllowEmpty && g.ClassBase is not ("RequiredString" or "RequiredGuid"))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.AllowEmptyOnNumericOrDateRequired,
                    title: "[AllowEmpty] is only valid on RequiredString and RequiredGuid",
                    messageFormat: "Class '{0}' has [AllowEmpty] but inherits from '{1}'. [AllowEmpty] only applies to RequiredString and RequiredGuid; use [AllowZero] for numeric Required bases or [AllowMinValue] for date Required bases. RequiredBool / RequiredEnum have no opt-out (RequiredBool is degenerate; RequiredEnum uses smart-enum lookup).",
                    category: "Trellis",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName,
                g.ClassBase));
            ok = false;
        }

        if (g.HasAllowMinValue && !isDateBase)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.AllowMinValueOnNonDateRequired,
                    title: "[AllowMinValue] is only valid on date Required bases",
                    messageFormat: "Class '{0}' has [AllowMinValue] but inherits from '{1}'. [AllowMinValue] only applies to RequiredDateTime and RequiredDateTimeOffset.",
                    category: "Trellis",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName,
                g.ClassBase));
            ok = false;
        }

        if (g.HasAllowWhitespace && g.ClassBase != "RequiredString")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.AllowWhitespaceOnNonStringRequired,
                    title: "[AllowWhitespace] is only valid on RequiredString",
                    messageFormat: "Class '{0}' has [AllowWhitespace] but inherits from '{1}'. [AllowWhitespace] only applies to RequiredString.",
                    category: "Trellis",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName,
                g.ClassBase));
            ok = false;
        }

        if (g.HasNoTrim && g.ClassBase != "RequiredString")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.NoTrimOnNonStringRequired,
                    title: "[NoTrim] is only valid on RequiredString",
                    messageFormat: "Class '{0}' has [NoTrim] but inherits from '{1}'. [NoTrim] only applies to RequiredString.",
                    category: "Trellis",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName,
                g.ClassBase));
            ok = false;
        }

        if (g.HasAllowZero && (g.HasPositive || g.HasNegative) && isNumericBase)
        {
            var attrName = g.HasPositive && g.HasNegative ? "[Positive] and [Negative]"
                : g.HasPositive ? "[Positive]"
                : "[Negative]";
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.ContradictoryRequiredAttributeCombination,
                    title: "Contradictory Required attribute combination",
                    messageFormat: "Class '{0}' has [AllowZero] combined with {1}. {1} rejects zero by definition, so [AllowZero] is contradictory. Remove [AllowZero], or use [NonNegative]/[NonPositive] if zero should be accepted.",
                    category: "Trellis",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName,
                attrName));
            ok = false;
        }

        // Numeric convenience attrs ([Positive] / [NonNegative] / [Negative] / [NonPositive])
        // only make sense on numeric Required bases (Int, Long, Decimal).
        if (numericConvenienceCount > 0 && !isNumericBase)
        {
            var attrName = g.HasPositive ? "[Positive]"
                : g.HasNonNegative ? "[NonNegative]"
                : g.HasNegative ? "[Negative]"
                : "[NonPositive]";
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.NumericConvenienceOnNonNumeric,
                    title: "Numeric convenience attribute on non-numeric Required base",
                    messageFormat: "Class '{0}' has {1} but inherits from '{2}'. The numeric convenience attributes ([Positive], [NonNegative], [Negative], [NonPositive]) only apply to RequiredInt, RequiredLong, and RequiredDecimal.",
                    category: "Trellis",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName,
                attrName,
                g.ClassBase));
            ok = false;
        }

        if (numericConvenienceCount > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.NumericConvenienceConflict,
                    title: "Conflicting numeric convenience attributes",
                    messageFormat: "Class '{0}' carries more than one of [Positive] / [NonNegative] / [Negative] / [NonPositive]. Pick exactly one — the sign constraints are mutually exclusive.",
                    category: "Trellis",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName));
            ok = false;
        }

        // Convenience attr + explicit [Range] is a conflict because explicit [Range] silently
        // overrides the convenience sign check during synthesis. Emit TRLS045 instead of
        // letting `[Positive, Range(0, 100)]` silently accept 0.
        if (numericConvenienceCount > 0 && g.HasExplicitRange && isNumericBase)
        {
            var attrName = g.HasPositive ? "[Positive]"
                : g.HasNonNegative ? "[NonNegative]"
                : g.HasNegative ? "[Negative]"
                : "[NonPositive]";
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.NumericConvenienceWithExplicitRange,
                    title: "Numeric convenience attribute combined with explicit [Range]",
                    messageFormat: "Class '{0}' has {1} combined with an explicit [Range]. The combination would silently disable the convenience sign check — pick one. Use [Range] alone for bounded values, or {1} alone for an unbounded sign constraint.",
                    category: "Trellis",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName,
                attrName));
            ok = false;
        }

        return ok;
    }

    private static string? GenerateStringMethods(RequiredPartialClassInfo g, SourceProductionContext context, HashSet<string> reportedCollisions)
    {
        // Validate [StringLength] constraints are consistent
        if (g.MinLength.HasValue && g.MaxLength.HasValue && g.MinLength.Value > g.MaxLength.Value)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.InvalidStringLengthRange,
                    title: "StringLength MinimumLength exceeds MaximumLength",
                    messageFormat: "Class '{0}' has [StringLength({1}, MinimumLength = {2})] where MinimumLength exceeds MaximumLength. No value can satisfy both constraints.",
                    category: "Trellis",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName,
                g.MaxLength.Value,
                g.MinLength.Value));
            return null;
        }

        // Build length validation checks if [StringLength] is applied (checked after trim)
        var lengthChecks = "";

        if (g.MinLength.HasValue)
        {
            lengthChecks += $@"
    if (normalized.Length < {g.MinLength.Value})
        return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {g.MinLength.Value} {"character" + (g.MinLength.Value == 1 ? "" : "s")}."" }})));";
        }

        if (g.MaxLength.HasValue)
        {
            lengthChecks += $@"
    if (normalized.Length > {g.MaxLength.Value})
        return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be {g.MaxLength.Value} {"character" + (g.MaxLength.Value == 1 ? "" : "s")} or fewer."" }})));";
        }

        // String validation pipeline, strict-by-default:
        //   1. Null check (always emitted; no opt-out).
        //   2. Raw-input whitespace-only detection and rejection (unless [AllowWhitespace]).
        //   3. Trim (unless [NoTrim]).
        //   4. Empty-string rejection (unless [AllowEmpty], or [AllowWhitespace] accepted raw whitespace that trimmed to empty).
        //   5. [StringLength] (operates on the normalized value).
        //   6. ValidateAdditional consumer hook.
        var needsWhitespaceOnlyProbe = !g.HasAllowWhitespace
            || (g.HasAllowWhitespace && !g.HasAllowEmpty && !g.HasNoTrim);
        var whitespaceOnlyStep = needsWhitespaceOnlyProbe
            ? $@"
    bool isWhitespaceOnly = false;
    if (value.Length > 0)
    {{
        isWhitespaceOnly = true;
        for (int i = 0; i < value.Length; i++)
        {{
            if (!char.IsWhiteSpace(value[i]))
            {{
                isWhitespaceOnly = false;
                break;
            }}
        }}
    }}"
            : "";
        var whitespaceOnlyReject = g.HasAllowWhitespace
            ? ""
            : $@"
    if (isWhitespaceOnly)
        return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be whitespace-only."" }})));";
        var trimStep = g.HasNoTrim
            ? "var normalized = value;"
            : "var normalized = value.Trim();";
        var emptyStep = "";
        if (!g.HasAllowEmpty)
        {
            var emptyCondition = g.HasAllowWhitespace && !g.HasNoTrim
                ? "normalized.Length == 0 && !isWhitespaceOnly"
                : "normalized.Length == 0";
            emptyStep = $@"
    if ({emptyCondition})
        return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));";
        }

        return $@"

        /// <summary>
        /// Optional validation hook. Implement this partial method to add custom validation
        /// (e.g., regex patterns, format checks). Called after built-in validations pass.
        /// </summary>
        /// <param name=""value"">The validated string value (trimmed unless [NoTrim] is applied).</param>
        /// <param name=""fieldName"">The normalized field name for error messages.</param>
        /// <param name=""errorMessage"">Set to a non-null string to reject the value.</param>
        static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage);

        /// <summary>
        /// Creates a validated instance from a string.
        /// Required by IScalarValue interface for model binding and JSON deserialization.
        /// </summary>
        /// <param name=""value"">The string value to validate.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(string? value, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            if (value is null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be null."" }})));
            {whitespaceOnlyStep}{whitespaceOnlyReject}
            {trimStep}{emptyStep}{lengthChecks}
            string? additionalError = null;
            ValidateAdditional(normalized, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(normalized));
        }}

        /// <summary>
        /// Creates a validated instance from a string. Throws if validation fails.
        /// </summary>
        /// <param name=""value"">The string value to validate.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>The validated value object.</returns>
        /// <exception cref=""InvalidOperationException"">Thrown when validation fails.</exception>
        public static {g.ClassName} Create(string? value, string? fieldName = null)
        {{
            var result = TryCreate(value, fieldName);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}";
    }

    private static string? GenerateIntMethods(RequiredPartialClassInfo g, SourceProductionContext context, HashSet<string> reportedCollisions)
    {
        var result = "";
        var hasRange = g.RangeMin.HasValue && g.RangeMax.HasValue;
        var rangeMin = g.RangeMin.GetValueOrDefault();
        var rangeMax = g.RangeMax.GetValueOrDefault();

        var notDefaultDetail = $@"""{g.ClassName.SplitPascalCase()} cannot be zero.""";
        var notDefaultIfCheck = g.HasAllowZero
            ? ""
            : $@"
            if (value == 0)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})));";
        var notDefaultNullableEnsure = g.HasAllowZero
            ? ""
            : $@"
                .Ensure(x => x != 0, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})))";
        var notDefaultParsedEnsure = g.HasAllowZero
            ? ""
            : $@"
                .Ensure(_ => parsedInt != 0, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})))";

        // Validate [Range] constraints are consistent
        if (hasRange && rangeMin > rangeMax)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.InvalidRangeMinExceedsMax,
                    title: "Range Minimum exceeds Maximum",
                    messageFormat: "Class '{0}' has [Range({1}, {2})] where Minimum exceeds Maximum. No value can satisfy both constraints.",
                    category: "Trellis",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName,
                rangeMin,
                rangeMax));
            return null;
        }

        if (hasRange)
        {
            // Range-validated TryCreate overloads
            result += $@"

        /// <summary>
        /// Optional validation hook. Implement this partial method to add custom validation.
        /// Called after built-in validations (null, range) pass.
        /// </summary>
        /// <param name=""value"">The validated integer value.</param>
        /// <param name=""fieldName"">The normalized field name for error messages.</param>
        /// <param name=""errorMessage"">Set to a non-null string to reject the value.</param>
        static partial void ValidateAdditional(int value, string fieldName, ref string? errorMessage);

        /// <summary>
        /// Creates a validated instance from an integer.
        /// Required by IScalarValue interface for model binding and JSON deserialization.
        /// </summary>
        /// <param name=""value"">The integer value to validate.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(int value, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");{notDefaultIfCheck}
            if (value < {rangeMin})
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {rangeMin}."" }})));
            if (value > {rangeMax})
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {rangeMax}."" }})));
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(int? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }}))){notDefaultNullableEnsure}
                .Ensure(x => x >= {rangeMin}, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {rangeMin}."" }})))
                .Ensure(x => x <= {rangeMax}, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {rangeMax}."" }})));
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            int parsedInt = 0;
            var validated = stringOrNull
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => int.TryParse(x, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsedInt), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid integer."" }}))){notDefaultParsedEnsure}
                .Ensure(_ => parsedInt >= {rangeMin}, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {rangeMin}."" }})))
                .Ensure(_ => parsedInt <= {rangeMax}, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {rangeMax}."" }})));
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedInt, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(_ => new {g.ClassName}(parsedInt));
        }}";
        }
        else
        {
            // Default TryCreate overloads (no [Range] — accepts any int including zero)
            result += $@"

        /// <summary>
        /// Optional validation hook. Implement this partial method to add custom validation.
        /// Called after built-in validations (null-check) pass.
        /// </summary>
        /// <param name=""value"">The validated integer value.</param>
        /// <param name=""fieldName"">The normalized field name for error messages.</param>
        /// <param name=""errorMessage"">Set to a non-null string to reject the value.</param>
        static partial void ValidateAdditional(int value, string fieldName, ref string? errorMessage);

        /// <summary>
        /// Creates a validated instance from an integer.
        /// Required by IScalarValue interface for model binding and JSON deserialization.
        /// </summary>
        /// <param name=""value"">The integer value to validate.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(int value, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");{notDefaultIfCheck}
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(int? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }}))){notDefaultNullableEnsure};
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            int parsedInt = 0;
            var validated = stringOrNull
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => int.TryParse(x, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsedInt), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid integer."" }}))){notDefaultParsedEnsure};
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedInt, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(_ => new {g.ClassName}(parsedInt));
        }}";
        }

        // IFormattableScalarValue TryCreate overload for culture-sensitive parsing
        result += $@"

        /// <summary>
        /// Attempts to create a validated instance from a string using the specified format provider.
        /// Use for culture-sensitive parsing of integer values.
        /// </summary>
        /// <param name=""value"">The string value to parse.</param>
        /// <param name=""provider"">The format provider for culture-sensitive parsing. Defaults to InvariantCulture when null.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            if (value is null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, provider ?? System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid integer."" }})));
            return TryCreate(parsed, fieldName);
        }}";

        // Create and Parse are the same regardless of [Range]
        result += $@"

        /// <summary>
        /// Creates a validated instance from an integer. Throws if validation fails.
        /// Use this for known-valid values in tests or with constants.
        /// </summary>
        /// <param name=""value"">The integer value to validate.</param>
        /// <returns>The validated value object.</returns>
        /// <exception cref=""InvalidOperationException"">Thrown when validation fails.</exception>
        public static new {g.ClassName} Create(int value)
        {{
            var result = TryCreate(value, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}

        /// <summary>
        /// Creates a validated instance from a string by parsing it as an integer. Throws if validation or parsing fails.
        /// Use this for known-valid integer strings in tests or with constants.
        /// </summary>
        /// <param name=""stringValue"">The string value to parse and validate.</param>
        /// <returns>The validated value object.</returns>
        /// <exception cref=""InvalidOperationException"">Thrown when validation or parsing fails.</exception>
        public static {g.ClassName} Create(string stringValue)
        {{
            var result = TryCreate(stringValue, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}";

        return result;
    }

    private static string? GenerateDecimalMethods(RequiredPartialClassInfo g, SourceProductionContext context, HashSet<string> reportedCollisions)
    {
        var result = "";
        var hasRange = g.RangeDoubleMin.HasValue && g.RangeDoubleMax.HasValue;
        // Use double values for fractional ranges, fall back to int values
        var rangeMinD = g.RangeDoubleMin ?? (double?)g.RangeMin;
        var rangeMaxD = g.RangeDoubleMax ?? (double?)g.RangeMax;
        hasRange = hasRange || (g.RangeMin.HasValue && g.RangeMax.HasValue);

        var notDefaultDetail = $@"""{g.ClassName.SplitPascalCase()} cannot be zero.""";
        var notDefaultIfCheck = g.HasAllowZero
            ? ""
            : $@"
            if (value == 0m)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})));";
        var notDefaultNullableEnsure = g.HasAllowZero
            ? ""
            : $@"
                .Ensure(x => x != 0m, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})))";
        var notDefaultParsedEnsure = g.HasAllowZero
            ? ""
            : $@"
                .Ensure(_ => parsedDecimal != 0m, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})))";

        // Convenience sign-check attributes ([Positive] / [NonNegative] / [Negative] /
        // [NonPositive]) are emitted as direct sign comparisons rather than as Range bounds
        // because the decimal value range (±7.9e28) exceeds what double can round-trip
        // safely through FormatDecimalLiteral. The check is appended after notDefaultIfCheck
        // so a [NotDefault]+[Positive]-style stack still surfaces the strictest message first.
        (string ifCheck, string nullableEnsure, string parsedEnsure) signCheck =
            BuildDecimalConvenienceSignCheck(g);

        // Validate [Range] constraints are consistent
        if (hasRange && rangeMinD > rangeMaxD)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.InvalidRangeMinExceedsMax,
                    title: "Range Minimum exceeds Maximum",
                    messageFormat: "Class '{0}' has [Range({1}, {2})] where Minimum exceeds Maximum. No value can satisfy both constraints.",
                    category: "Trellis",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName,
                rangeMinD,
                rangeMaxD));
            return null;
        }

        if (hasRange)
        {
            var minStr = FormatDecimalLiteral(rangeMinD.GetValueOrDefault());
            var maxStr = FormatDecimalLiteral(rangeMaxD.GetValueOrDefault());

            // Validate range values fit in decimal
            if (minStr is null || maxStr is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        id: Ids.DecimalRangeExceedsDecimalRange,
                        title: "Range value exceeds decimal range",
                        messageFormat: "Class '{0}' has [Range] values that exceed the decimal type range (±7.9×10²⁸). Use ValidateAdditional for bounds that exceed decimal range.",
                        category: "Trellis",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    location: null,
                    g.ClassName));
                return null;
            }

            // Range-validated TryCreate overloads
            result += $@"

        /// <summary>
        /// Optional validation hook. Implement this partial method to add custom validation.
        /// Called after built-in validations (null, range) pass.
        /// </summary>
        /// <param name=""value"">The validated decimal value.</param>
        /// <param name=""fieldName"">The normalized field name for error messages.</param>
        /// <param name=""errorMessage"">Set to a non-null string to reject the value.</param>
        static partial void ValidateAdditional(decimal value, string fieldName, ref string? errorMessage);

        /// <summary>
        /// Creates a validated instance from a decimal.
        /// Required by IScalarValue interface for model binding and JSON deserialization.
        /// </summary>
        /// <param name=""value"">The decimal value to validate.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(decimal value, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");{notDefaultIfCheck}
            if (value < {minStr}m)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {minStr}."" }})));
            if (value > {maxStr}m)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {maxStr}."" }})));
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(decimal? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }}))){notDefaultNullableEnsure}
                .Ensure(x => x >= {minStr}m, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {minStr}."" }})))
                .Ensure(x => x <= {maxStr}m, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {maxStr}."" }})));
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            decimal parsedDecimal = 0m;
            var validated = stringOrNull
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => decimal.TryParse(x, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out parsedDecimal), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid decimal."" }}))){notDefaultParsedEnsure}
                .Ensure(_ => parsedDecimal >= {minStr}m, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {minStr}."" }})))
                .Ensure(_ => parsedDecimal <= {maxStr}m, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {maxStr}."" }})));
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedDecimal, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(_ => new {g.ClassName}(parsedDecimal));
        }}";
        }
        else
        {
            // Default TryCreate overloads (no [Range])
            result += $@"

        /// <summary>
        /// Optional validation hook. Implement this partial method to add custom validation.
        /// Called after built-in validations (null-check) pass.
        /// </summary>
        /// <param name=""value"">The validated decimal value.</param>
        /// <param name=""fieldName"">The normalized field name for error messages.</param>
        /// <param name=""errorMessage"">Set to a non-null string to reject the value.</param>
        static partial void ValidateAdditional(decimal value, string fieldName, ref string? errorMessage);

        /// <summary>
        /// Creates a validated instance from a decimal.
        /// Required by IScalarValue interface for model binding and JSON deserialization.
        /// </summary>
        /// <param name=""value"">The decimal value to validate.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(decimal value, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");{notDefaultIfCheck}{signCheck.ifCheck}
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(decimal? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }}))){notDefaultNullableEnsure}{signCheck.nullableEnsure};
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            decimal parsedDecimal = 0m;
            var validated = stringOrNull
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => decimal.TryParse(x, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out parsedDecimal), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid decimal."" }}))){notDefaultParsedEnsure}{signCheck.parsedEnsure};
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedDecimal, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(_ => new {g.ClassName}(parsedDecimal));
        }}";
        }

        // IFormattableScalarValue TryCreate overload for culture-sensitive parsing
        result += $@"

        /// <summary>
        /// Attempts to create a validated instance from a string using the specified format provider.
        /// Use for culture-sensitive parsing of decimal values.
        /// </summary>
        /// <param name=""value"">The string value to parse.</param>
        /// <param name=""provider"">The format provider for culture-sensitive parsing. Defaults to InvariantCulture when null.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            if (value is null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (!decimal.TryParse(value, System.Globalization.NumberStyles.Number, provider ?? System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid decimal."" }})));
            return TryCreate(parsed, fieldName);
        }}";

        // Create and Parse are the same regardless of [Range]
        result += $@"

        /// <summary>
        /// Creates a validated instance from a decimal. Throws if validation fails.
        /// Use this for known-valid values in tests or with constants.
        /// </summary>
        /// <param name=""value"">The decimal value to validate.</param>
        /// <returns>The validated value object.</returns>
        /// <exception cref=""InvalidOperationException"">Thrown when validation fails.</exception>
        public static new {g.ClassName} Create(decimal value)
        {{
            var result = TryCreate(value, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}

        /// <summary>
        /// Creates a validated instance from a string by parsing it as a decimal. Throws if validation or parsing fails.
        /// Use this for known-valid decimal strings in tests or with constants.
        /// </summary>
        /// <param name=""stringValue"">The string value to parse and validate.</param>
        /// <returns>The validated value object.</returns>
        /// <exception cref=""InvalidOperationException"">Thrown when validation or parsing fails.</exception>
        public static {g.ClassName} Create(string stringValue)
        {{
            var result = TryCreate(stringValue, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}";

        return result;
    }

    private static (string ifCheck, string nullableEnsure, string parsedEnsure) BuildDecimalConvenienceSignCheck(RequiredPartialClassInfo g)
    {
        // Pick the active convenience attribute (mutual exclusion is enforced by
        // ValidateAttributeUsage); if multiple slipped through, ValidateAttributeUsage already
        // emitted TRLS044 and generation will be skipped, so the order below is defensive.
        if (!g.HasPositive && !g.HasNonNegative && !g.HasNegative && !g.HasNonPositive)
            return ("", "", "");

        string compareOp;
        string message;
        if (g.HasPositive) { compareOp = "<= 0m"; message = $"{g.ClassName.SplitPascalCase()} must be positive."; }
        else if (g.HasNonNegative) { compareOp = "< 0m"; message = $"{g.ClassName.SplitPascalCase()} must be zero or positive."; }
        else if (g.HasNegative) { compareOp = ">= 0m"; message = $"{g.ClassName.SplitPascalCase()} must be negative."; }
        else /* HasNonPositive */ { compareOp = "> 0m"; message = $"{g.ClassName.SplitPascalCase()} must be zero or negative."; }

        var detail = $@"""{message}""";

        var ifCheck = $@"
            if (value {compareOp})
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {detail} }})));";

        var nullableEnsure = $@"
                .Ensure(x => !(x {compareOp}), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {detail} }})))";

        var parsedEnsure = $@"
                .Ensure(_ => !(parsedDecimal {compareOp}), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {detail} }})))";

        return (ifCheck, nullableEnsure, parsedEnsure);
    }

    private static string? GenerateLongMethods(RequiredPartialClassInfo g, SourceProductionContext context, HashSet<string> reportedCollisions)
    {
        var result = "";
        var hasRange = g.RangeLongMin.HasValue && g.RangeLongMax.HasValue;
        var rangeLongMin = g.RangeLongMin.GetValueOrDefault();
        var rangeLongMax = g.RangeLongMax.GetValueOrDefault();

        var notDefaultDetail = $@"""{g.ClassName.SplitPascalCase()} cannot be zero.""";
        var notDefaultIfCheck = g.HasAllowZero
            ? ""
            : $@"
            if (value == 0L)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})));";
        var notDefaultNullableEnsure = g.HasAllowZero
            ? ""
            : $@"
                .Ensure(x => x != 0L, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})))";
        var notDefaultParsedEnsure = g.HasAllowZero
            ? ""
            : $@"
                .Ensure(_ => parsedLong != 0L, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})))";

        // Validate [Range] constraints are consistent
        if (hasRange && rangeLongMin > rangeLongMax)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: Ids.InvalidRangeMinExceedsMax,
                    title: "Range Minimum exceeds Maximum",
                    messageFormat: "Class '{0}' has [Range({1}, {2})] where Minimum exceeds Maximum. No value can satisfy both constraints.",
                    category: "Trellis",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location: null,
                g.ClassName,
                rangeLongMin,
                rangeLongMax));
            return null;
        }

        if (hasRange)
        {
            // Range-validated TryCreate overloads
            result += $@"

        /// <summary>
        /// Optional validation hook. Implement this partial method to add custom validation.
        /// Called after built-in validations (null, range) pass.
        /// </summary>
        /// <param name=""value"">The validated long value.</param>
        /// <param name=""fieldName"">The normalized field name for error messages.</param>
        /// <param name=""errorMessage"">Set to a non-null string to reject the value.</param>
        static partial void ValidateAdditional(long value, string fieldName, ref string? errorMessage);

        /// <summary>
        /// Creates a validated instance from a long.
        /// Required by IScalarValue interface for model binding and JSON deserialization.
        /// </summary>
        /// <param name=""value"">The long value to validate.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(long value, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");{notDefaultIfCheck}
            if (value < {rangeLongMin}L)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {rangeLongMin}."" }})));
            if (value > {rangeLongMax}L)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {rangeLongMax}."" }})));
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(long? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }}))){notDefaultNullableEnsure}
                .Ensure(x => x >= {rangeLongMin}L, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {rangeLongMin}."" }})))
                .Ensure(x => x <= {rangeLongMax}L, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {rangeLongMax}."" }})));
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            long parsedLong = 0;
            var validated = stringOrNull
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => long.TryParse(x, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsedLong), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid long."" }}))){notDefaultParsedEnsure}
                .Ensure(_ => parsedLong >= {rangeLongMin}L, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {rangeLongMin}."" }})))
                .Ensure(_ => parsedLong <= {rangeLongMax}L, _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {rangeLongMax}."" }})));
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedLong, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(_ => new {g.ClassName}(parsedLong));
        }}";
        }
        else
        {
            // Default TryCreate overloads (no [Range] — accepts any long)
            result += $@"

        /// <summary>
        /// Optional validation hook. Implement this partial method to add custom validation.
        /// Called after built-in validations (null-check) pass.
        /// </summary>
        /// <param name=""value"">The validated long value.</param>
        /// <param name=""fieldName"">The normalized field name for error messages.</param>
        /// <param name=""errorMessage"">Set to a non-null string to reject the value.</param>
        static partial void ValidateAdditional(long value, string fieldName, ref string? errorMessage);

        /// <summary>
        /// Creates a validated instance from a long.
        /// Required by IScalarValue interface for model binding and JSON deserialization.
        /// </summary>
        /// <param name=""value"">The long value to validate.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(long value, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");{notDefaultIfCheck}
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(long? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }}))){notDefaultNullableEnsure};
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            long parsedLong = 0;
            var validated = stringOrNull
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => long.TryParse(x, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsedLong), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid long."" }}))){notDefaultParsedEnsure};
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedLong, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(_ => new {g.ClassName}(parsedLong));
        }}";
        }

        // IFormattableScalarValue TryCreate overload for culture-sensitive parsing
        result += $@"

        /// <summary>
        /// Attempts to create a validated instance from a string using the specified format provider.
        /// Use for culture-sensitive parsing of long values.
        /// </summary>
        /// <param name=""value"">The string value to parse.</param>
        /// <param name=""provider"">The format provider for culture-sensitive parsing. Defaults to InvariantCulture when null.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            if (value is null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (!long.TryParse(value, System.Globalization.NumberStyles.Integer, provider ?? System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid long."" }})));
            return TryCreate(parsed, fieldName);
        }}";

        // Create and Parse are the same regardless of [Range]
        result += $@"

        /// <summary>
        /// Creates a validated instance from a long. Throws if validation fails.
        /// Use this for known-valid values in tests or with constants.
        /// </summary>
        /// <param name=""value"">The long value to validate.</param>
        /// <returns>The validated value object.</returns>
        /// <exception cref=""InvalidOperationException"">Thrown when validation fails.</exception>
        public static new {g.ClassName} Create(long value)
        {{
            var result = TryCreate(value, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}

        /// <summary>
        /// Creates a validated instance from a string by parsing it as a long. Throws if validation or parsing fails.
        /// Use this for known-valid long strings in tests or with constants.
        /// </summary>
        /// <param name=""stringValue"">The string value to parse and validate.</param>
        /// <returns>The validated value object.</returns>
        /// <exception cref=""InvalidOperationException"">Thrown when validation or parsing fails.</exception>
        public static {g.ClassName} Create(string stringValue)
        {{
            var result = TryCreate(stringValue, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}";

        return result;
    }

    private static string GenerateBoolMethods(RequiredPartialClassInfo g, SourceProductionContext context, HashSet<string> reportedCollisions) =>
        $@"

        /// <summary>
        /// Optional validation hook. Implement this partial method to add custom validation.
        /// Called after built-in validations (null-check) pass.
        /// </summary>
        /// <param name=""value"">The validated boolean value.</param>
        /// <param name=""fieldName"">The normalized field name for error messages.</param>
        /// <param name=""errorMessage"">Set to a non-null string to reject the value.</param>
        static partial void ValidateAdditional(bool value, string fieldName, ref string? errorMessage);

        /// <summary>
        /// Creates a validated instance from a boolean.
        /// Required by IScalarValue interface for model binding and JSON deserialization.
        /// </summary>
        /// <param name=""value"">The boolean value.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object.</returns>
        public static Result<{g.ClassName}> TryCreate(bool value, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(bool? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            bool parsedBool = false;
            var validated = stringOrNull
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => bool.TryParse(x, out parsedBool), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid boolean (true or false)."" }})));
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedBool, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(_ => new {g.ClassName}(parsedBool));
        }}

        /// <summary>
        /// Creates a validated instance from a boolean. Throws if validation fails.
        /// Use this for known-valid values in tests or with constants.
        /// </summary>
        /// <param name=""value"">The boolean value.</param>
        /// <returns>The validated value object.</returns>
        /// <exception cref=""InvalidOperationException"">Thrown when validation fails.</exception>
        public static new {g.ClassName} Create(bool value)
        {{
            var result = TryCreate(value, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}

        /// <summary>
        /// Creates a validated instance from a string by parsing it as a boolean. Throws if validation or parsing fails.
        /// Use this for known-valid boolean strings in tests or with constants.
        /// </summary>
        /// <param name=""stringValue"">The string value to parse and validate.</param>
        /// <returns>The validated value object.</returns>
        /// <exception cref=""InvalidOperationException"">Thrown when validation or parsing fails.</exception>
        public static {g.ClassName} Create(string stringValue)
        {{
            var result = TryCreate(stringValue, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}";

    private static string GenerateDateTimeMethods(RequiredPartialClassInfo g, SourceProductionContext context, HashSet<string> reportedCollisions)
    {
        var notDefaultDetail = $@"""{g.ClassName.SplitPascalCase()} cannot be DateTime.MinValue.""";
        var notDefaultCheck = g.HasAllowMinValue
            ? ""
            : $@"
            if (value == default(DateTime))
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})));";
        var notDefaultNullableEnsure = g.HasAllowMinValue
            ? ""
            : $@"
                .Ensure(x => x != default(DateTime), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})))";
        var notDefaultParsedEnsure = g.HasAllowMinValue
            ? ""
            : $@"
                .Ensure(_ => parsedDateTime != default(DateTime), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})))";

        return $@"

        /// <summary>
        /// Optional validation hook. Implement this partial method to add custom validation.
        /// Called after built-in validations pass.
        /// </summary>
        /// <param name=""value"">The validated DateTime value.</param>
        /// <param name=""fieldName"">The normalized field name for error messages.</param>
        /// <param name=""errorMessage"">Set to a non-null string to reject the value.</param>
        static partial void ValidateAdditional(DateTime value, string fieldName, ref string? errorMessage);

        /// <summary>
        /// Creates a validated instance from a DateTime.
        /// Required by IScalarValue interface for model binding and JSON deserialization.
        /// </summary>
        /// <param name=""value"">The DateTime value to validate.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(DateTime value, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");{notDefaultCheck}
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(DateTime? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }}))){notDefaultNullableEnsure};
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            DateTime parsedDateTime = default;
            var validated = stringOrNull
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => DateTime.TryParse(x, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out parsedDateTime), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid date/time."" }}))){notDefaultParsedEnsure};
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedDateTime, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(_ => new {g.ClassName}(parsedDateTime));
        }}

        /// <summary>
        /// Attempts to create a validated instance from a string using the specified format provider.
        /// Use for culture-sensitive parsing of date/time values.
        /// </summary>
        /// <param name=""value"">The string value to parse.</param>
        /// <param name=""provider"">The format provider for culture-sensitive parsing. Defaults to InvariantCulture when null.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            if (value is null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (!DateTime.TryParse(value, provider ?? System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid date/time."" }})));
            return TryCreate(parsed, fieldName);
        }}

        /// <summary>
        /// Creates a validated instance from a DateTime. Throws if validation fails.
        /// </summary>
        public static new {g.ClassName} Create(DateTime value)
        {{
            var result = TryCreate(value, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}

        /// <summary>
        /// Creates a validated instance from a string by parsing it as a DateTime. Throws if validation or parsing fails.
        /// </summary>
        public static {g.ClassName} Create(string stringValue)
        {{
            var result = TryCreate(stringValue, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}";
    }

    private static string GenerateDateTimeOffsetMethods(RequiredPartialClassInfo g, SourceProductionContext context, HashSet<string> reportedCollisions)
    {
        var notDefaultDetail = $@"""{g.ClassName.SplitPascalCase()} cannot be DateTimeOffset.MinValue.""";
        var notDefaultCheck = g.HasAllowMinValue
            ? ""
            : $@"
            if (value == default(DateTimeOffset))
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})));";
        var notDefaultNullableEnsure = g.HasAllowMinValue
            ? ""
            : $@"
                .Ensure(x => x != default(DateTimeOffset), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})))";
        var notDefaultParsedEnsure = g.HasAllowMinValue
            ? ""
            : $@"
                .Ensure(_ => parsedDateTimeOffset != default(DateTimeOffset), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = {notDefaultDetail} }})))";

        return $@"

        /// <summary>
        /// Optional validation hook. Implement this partial method to add custom validation.
        /// Called after built-in validations pass.
        /// </summary>
        /// <param name=""value"">The validated DateTimeOffset value.</param>
        /// <param name=""fieldName"">The normalized field name for error messages.</param>
        /// <param name=""errorMessage"">Set to a non-null string to reject the value.</param>
        static partial void ValidateAdditional(DateTimeOffset value, string fieldName, ref string? errorMessage);

        /// <summary>
        /// Creates a validated instance from a DateTimeOffset.
        /// Required by IScalarValue interface for model binding and JSON deserialization.
        /// </summary>
        /// <param name=""value"">The DateTimeOffset value to validate.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(DateTimeOffset value, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");{notDefaultCheck}
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(DateTimeOffset? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }}))){notDefaultNullableEnsure};
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            DateTimeOffset parsedDateTimeOffset = default;
            var validated = stringOrNull
                .ToResult(() => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => DateTimeOffset.TryParse(x, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out parsedDateTimeOffset), _ => new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid date/time with offset."" }}))){notDefaultParsedEnsure};
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedDateTimeOffset, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(_ => new {g.ClassName}(parsedDateTimeOffset));
        }}

        /// <summary>
        /// Attempts to create a validated instance from a string using the specified format provider.
        /// Use for culture-sensitive parsing of DateTimeOffset values.
        /// </summary>
        /// <param name=""value"">The string value to parse.</param>
        /// <param name=""provider"">The format provider for culture-sensitive parsing. Defaults to InvariantCulture when null.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            if (value is null)
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (!DateTimeOffset.TryParse(value, provider ?? System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                return Result.Fail<{g.ClassName}>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid date/time with offset."" }})));
            return TryCreate(parsed, fieldName);
        }}

        /// <summary>
        /// Creates a validated instance from a DateTimeOffset. Throws if validation fails.
        /// </summary>
        public static new {g.ClassName} Create(DateTimeOffset value)
        {{
            var result = TryCreate(value, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}

        /// <summary>
        /// Creates a validated instance from a string by parsing it as a DateTimeOffset. Throws if validation or parsing fails.
        /// </summary>
        public static {g.ClassName} Create(string stringValue)
        {{
            var result = TryCreate(stringValue, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}";
    }

    /// <summary>
    /// Extracts metadata from class declarations to determine which classes need code generation.
    /// </summary>
    /// <param name="compilation">The compilation containing semantic information.</param>
    /// <param name="classes">The candidate class declarations.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>
    /// A list of <see cref="RequiredPartialClassInfo"/> containing metadata for each class to generate.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method uses semantic analysis to extract:
    /// <list type="bullet">
    /// <item>Fully-qualified namespace</item>
    /// <item>Class name</item>
    /// <item>Base class name (RequiredGuid, RequiredString, RequiredInt, etc.)</item>
    /// <item>Accessibility level (public, internal, etc.)</item>
    /// </list>
    /// </para>
    /// <para>
    /// The method respects cancellation to avoid blocking the IDE during long compilations.
    /// </para>
    /// </remarks>
    private static List<RequiredPartialClassInfo> GetTypesToGenerate(Compilation compilation, IEnumerable<ClassDeclarationSyntax> classes, CancellationToken cancellationToken)
    {
        var classToGenerate = new List<RequiredPartialClassInfo>();

        foreach (var classDeclarationSyntax in classes)
        {
            // stop if we're asked to
            cancellationToken.ThrowIfCancellationRequested();

            INamedTypeSymbol? classSymbol = compilation
                .GetSemanticModel(classDeclarationSyntax.SyntaxTree)
                .GetDeclaredSymbol(classDeclarationSyntax, cancellationToken) as INamedTypeSymbol;

            if (classSymbol == null) continue;

            string className = classSymbol.Name;
            string @namespace = classSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : classSymbol.ContainingNamespace.ToDisplayString();
            // Extract just the base class name without type parameters (e.g., "RequiredGuid" from "RequiredGuid<EmployeeId>")
            string @base = classSymbol.BaseType?.Name ?? "unknown";
            string accessibility = AccessibilityToString(classSymbol.DeclaredAccessibility);
            var nestingParents = GetNestingParents(classSymbol);
            var typePath = BuildTypePath(@namespace, classSymbol);
            var userDeclaredMembers = GetUserDeclaredMembers(classSymbol, cancellationToken);

            // Read [StringLength] attribute for RequiredString types
            int? maxLength = null;
            int? minLength = null;
            if (@base == "RequiredString")
            {
                foreach (var attr in classSymbol.GetAttributes())
                {
                    if (attr.AttributeClass?.Name == "StringLengthAttribute"
                        && attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Trellis")
                    {
                        // Constructor: StringLengthAttribute(int maximumLength)
                        if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is int max)
                            maxLength = max;

                        // Named argument: MinimumLength = 3
                        foreach (var named in attr.NamedArguments)
                        {
                            if (named.Key == "MinimumLength" && named.Value.Value is int min && min > 0)
                                minLength = min;
                        }
                    }
                }
            }

            // Read [Range] attribute for numeric types (RequiredInt, RequiredDecimal, RequiredLong)
            int? rangeMin = null;
            int? rangeMax = null;
            long? rangeLongMin = null;
            long? rangeLongMax = null;
            double? rangeDoubleMin = null;
            double? rangeDoubleMax = null;
            if (@base is "RequiredInt" or "RequiredDecimal" or "RequiredLong")
            {
                foreach (var attr in classSymbol.GetAttributes())
                {
                    if (attr.AttributeClass?.Name == "RangeAttribute"
                        && attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Trellis")
                    {
                        if (attr.ConstructorArguments.Length >= 2)
                        {
                            var arg0 = attr.ConstructorArguments[0].Value;
                            var arg1 = attr.ConstructorArguments[1].Value;

                            if (@base == "RequiredLong")
                            {
                                rangeLongMin = arg0 switch { long l => l, int i => (long)i, _ => null };
                                rangeLongMax = arg1 switch { long l => l, int i => (long)i, _ => null };
                            }
                            else if (@base == "RequiredDecimal")
                            {
                                // RangeAttribute(double, double) or RangeAttribute(int, int)
                                rangeDoubleMin = arg0 switch { double d => d, int i => (double)i, _ => null };
                                rangeDoubleMax = arg1 switch { double d => d, int i => (double)i, _ => null };
                                // Also set int range for backward compat (int constructor)
                                if (arg0 is int minI) rangeMin = minI;
                                if (arg1 is int maxI) rangeMax = maxI;
                            }
                            else
                            {
                                if (arg0 is int min) rangeMin = min;
                                if (arg1 is int max) rangeMax = max;
                            }
                        }
                    }
                }
            }

            // Read [NotDefault], [Trim], opt-out marker attributes
            // ([AllowEmpty], [AllowWhitespace], [NoTrim], [AllowZero], [AllowMinValue], [AllowDefault]),
            // and numeric convenience attributes ([Positive], [NonNegative], [Negative], [NonPositive]).
            // [NotDefault] and [Trim] are vestigial under strict-by-default emission; they are
            // still tracked so ValidateAttributeUsage can report migration diagnostics.
            bool hasNotDefault = false;
            bool hasTrim = false;
            bool hasAllowEmpty = false;
            bool hasAllowWhitespace = false;
            bool hasNoTrim = false;
            bool hasAllowZero = false;
            bool hasAllowMinValue = false;
            bool hasAllowDefault = false;
            bool hasPositive = false;
            bool hasNonNegative = false;
            bool hasNegative = false;
            bool hasNonPositive = false;
            foreach (var attr in classSymbol.GetAttributes())
            {
                if (attr.AttributeClass?.ContainingNamespace?.ToDisplayString() != "Trellis")
                    continue;
                switch (attr.AttributeClass?.Name)
                {
                    case "NotDefaultAttribute":
                        hasNotDefault = true;
                        break;
                    case "TrimAttribute":
                        hasTrim = true;
                        break;
                    case "AllowEmptyAttribute":
                        hasAllowEmpty = true;
                        break;
                    case "AllowWhitespaceAttribute":
                        hasAllowWhitespace = true;
                        break;
                    case "NoTrimAttribute":
                        hasNoTrim = true;
                        break;
                    case "AllowZeroAttribute":
                        hasAllowZero = true;
                        break;
                    case "AllowMinValueAttribute":
                        hasAllowMinValue = true;
                        break;
                    case "AllowDefaultAttribute":
                        hasAllowDefault = true;
                        break;
                    case "PositiveAttribute":
                        hasPositive = true;
                        break;
                    case "NonNegativeAttribute":
                        hasNonNegative = true;
                        break;
                    case "NegativeAttribute":
                        hasNegative = true;
                        break;
                    case "NonPositiveAttribute":
                        hasNonPositive = true;
                        break;
                }
            }

            // Synthesize [Range] bounds from numeric convenience attributes when no explicit
            // [Range] was supplied. Explicit [Range] silently wins on synthesis, but the
            // (explicit + convenience) combination is itself a conflict — ValidateAttributeUsage
            // emits TRLS045 and skips generation so the silent-disable footgun is never reachable
            // in compiled code.
            var hasExplicitRange = rangeMin.HasValue || rangeMax.HasValue
                || rangeLongMin.HasValue || rangeLongMax.HasValue
                || rangeDoubleMin.HasValue || rangeDoubleMax.HasValue;
            ApplyNumericConvenienceRange(
                @base,
                hasPositive, hasNonNegative, hasNegative, hasNonPositive,
                ref rangeMin, ref rangeMax,
                ref rangeLongMin, ref rangeLongMax,
                ref rangeDoubleMin, ref rangeDoubleMax);

            classToGenerate.Add(new RequiredPartialClassInfo(
                @namespace, className, @base, accessibility,
                maxLength, minLength,
                rangeMin, rangeMax,
                rangeLongMin, rangeLongMax,
                rangeDoubleMin, rangeDoubleMax,
                nestingParents, typePath,
                hasNotDefault, hasTrim,
                hasAllowEmpty, hasAllowWhitespace, hasNoTrim, hasAllowZero, hasAllowMinValue, hasAllowDefault,
                hasPositive, hasNonNegative, hasNegative, hasNonPositive,
                hasExplicitRange,
                userDeclaredMembers));
        }

        return classToGenerate;
    }

    /// <summary>
    /// Translates the convenience numeric attributes (<c>[Positive]</c>, <c>[NonNegative]</c>,
    /// <c>[Negative]</c>, <c>[NonPositive]</c>) into the underlying <c>[Range]</c>-equivalent
    /// bounds the existing emission already understands, but only when no explicit <c>[Range]</c>
    /// is present for the corresponding numeric base. Explicit <c>[Range]</c> always wins —
    /// conflict diagnostics are emitted by <see cref="ValidateAttributeUsage"/> later.
    /// </summary>
    private static void ApplyNumericConvenienceRange(
        string @base,
        bool hasPositive, bool hasNonNegative, bool hasNegative, bool hasNonPositive,
        ref int? rangeMin, ref int? rangeMax,
        ref long? rangeLongMin, ref long? rangeLongMax,
        ref double? rangeDoubleMin, ref double? rangeDoubleMax)
    {
        if (!hasPositive && !hasNonNegative && !hasNegative && !hasNonPositive)
            return;

        if (@base == "RequiredInt")
        {
            if (rangeMin.HasValue || rangeMax.HasValue) return;
            if (hasPositive) { rangeMin = 1; rangeMax = int.MaxValue; }
            else if (hasNonNegative) { rangeMin = 0; rangeMax = int.MaxValue; }
            else if (hasNegative) { rangeMin = int.MinValue; rangeMax = -1; }
            else if (hasNonPositive) { rangeMin = int.MinValue; rangeMax = 0; }
        }
        else if (@base == "RequiredLong")
        {
            if (rangeLongMin.HasValue || rangeLongMax.HasValue) return;
            if (hasPositive) { rangeLongMin = 1L; rangeLongMax = long.MaxValue; }
            else if (hasNonNegative) { rangeLongMin = 0L; rangeLongMax = long.MaxValue; }
            else if (hasNegative) { rangeLongMin = long.MinValue; rangeLongMax = -1L; }
            else if (hasNonPositive) { rangeLongMin = long.MinValue; rangeLongMax = 0L; }
        }
        else if (@base == "RequiredDecimal")
        {
            // Convenience attrs on Decimal are handled as direct sign-checks in
            // GenerateDecimalMethods (see DecimalConvenienceSignCheck below) — not as Range
            // bounds — because the decimal value range (±7.9e28) exceeds what double can
            // round-trip safely through FormatDecimalLiteral, which would falsely trip TRLS034.
        }
        // For non-numeric bases the convenience attrs are no-ops; ValidateAttributeUsage emits
        // a diagnostic when they appear on a base that does not support them (see TRLS04x).
    }

    private static GeneratedMemberDeclaration[] GetUserDeclaredMembers(INamedTypeSymbol classSymbol, CancellationToken cancellationToken)
    {
        var declarations = new List<GeneratedMemberDeclaration>();

        foreach (var member in classSymbol.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member.IsImplicitlyDeclared)
                continue;

            var location = FirstSourceLocation(member);
            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.Ordinary } method:
                    if (IsPartialValidateAdditional(method, cancellationToken))
                        continue;
                    // Use MetadataName so generic methods (e.g. Create<T>) capture as "Create`1"
                    // and don't collide with the generator's non-generic Create.
                    declarations.Add(new GeneratedMemberDeclaration(
                        method.MetadataName,
                        MemberSignature(method.MetadataName, method.Parameters.Select(parameter => FormatParameterForSignature(parameter, classSymbol)).ToArray()),
                        matchesByNameOnly: false,
                        location));
                    break;
                case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                    declarations.Add(new GeneratedMemberDeclaration(
                        ".ctor",
                        MemberSignature(".ctor", constructor.Parameters.Select(parameter => FormatParameterForSignature(parameter, classSymbol)).ToArray()),
                        matchesByNameOnly: false,
                        location));
                    break;
                case IMethodSymbol { MethodKind: MethodKind.Conversion, MetadataName: "op_Explicit" } conversion:
                    // Conversion operators are distinguished by both source (parameter type) and
                    // target (return type). A user-declared `operator string(CustomerId)` is NOT
                    // the same as the generator-emitted `operator CustomerId(Guid)` and must not
                    // trigger a collision diagnostic.
                    var conversionSource = FormatTypeForSignature(conversion.Parameters[0].Type, classSymbol);
                    var conversionTarget = FormatTypeForSignature(conversion.ReturnType, classSymbol);
                    declarations.Add(new GeneratedMemberDeclaration(
                        "op_Explicit",
                        $"op_Explicit({conversionSource} -> {conversionTarget})",
                        matchesByNameOnly: false,
                        location));
                    break;
                case IPropertySymbol property:
                    declarations.Add(new GeneratedMemberDeclaration(property.Name, property.Name, matchesByNameOnly: true, location));
                    break;
                case IFieldSymbol field:
                    declarations.Add(new GeneratedMemberDeclaration(field.Name, field.Name, matchesByNameOnly: true, location));
                    break;
                case IEventSymbol @event:
                    declarations.Add(new GeneratedMemberDeclaration(@event.Name, @event.Name, matchesByNameOnly: true, location));
                    break;
            }
        }

        return declarations
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ThenBy(member => member.Signature, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsPartialValidateAdditional(IMethodSymbol method, CancellationToken cancellationToken)
    {
        if (method.Name != "ValidateAdditional")
            return false;

        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax(cancellationToken) is MethodDeclarationSyntax methodDeclaration
                && methodDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                return true;
        }

        return false;
    }

    private static Location? FirstSourceLocation(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault(static location => location.IsInSource) ?? symbol.Locations.FirstOrDefault();

    private static string FormatParameterForSignature(IParameterSymbol parameter, INamedTypeSymbol containingType)
    {
        var typeName = FormatTypeForSignature(parameter.Type, containingType);
        return parameter.RefKind switch
        {
            RefKind.Out => OutParameter(typeName),
            RefKind.Ref => RefParameter(typeName),
            RefKind.In => $"in {typeName}",
            _ => typeName
        };
    }

    private static string FormatTypeForSignature(ITypeSymbol type, INamedTypeSymbol containingType)
    {
        if (SymbolEqualityComparer.Default.Equals(type, containingType))
            return containingType.Name;

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType
            && nullableType.TypeArguments.Length == 1)
            return FormatTypeForSignature(nullableType.TypeArguments[0], containingType) + "?";

        return type.SpecialType switch
        {
            SpecialType.System_String => "string",
            SpecialType.System_Int32 => "int",
            SpecialType.System_Int64 => "long",
            SpecialType.System_Decimal => "decimal",
            SpecialType.System_Boolean => "bool",
            _ => type.ContainingNamespace.ToDisplayString() == "System" ? type.Name : type.Name
        };
    }
    private static string[] GetNestingParents(INamedTypeSymbol classSymbol)
    {
        var nestingParents = new List<string>();
        var parent = classSymbol.ContainingType;

        while (parent is not null)
        {
            nestingParents.Insert(0, BuildContainingTypeDeclaration(parent));
            parent = parent.ContainingType;
        }

        return nestingParents.ToArray();
    }

    private static string BuildTypePath(string @namespace, INamedTypeSymbol classSymbol)
    {
        var typeNames = new Stack<string>();
        var current = classSymbol;

        while (current is not null)
        {
            typeNames.Push(current.Name);
            current = current.ContainingType;
        }

        var typePath = string.Join(".", typeNames);
        return string.IsNullOrEmpty(@namespace) ? typePath : $"{@namespace}.{typePath}";
    }

    private static string BuildContainingTypeDeclaration(INamedTypeSymbol typeSymbol)
    {
        var parts = new List<string> { AccessibilityToString(typeSymbol.DeclaredAccessibility) };

        if (typeSymbol.IsStatic)
        {
            parts.Add("static");
        }
        else
        {
            if (typeSymbol.IsAbstract && typeSymbol.TypeKind == TypeKind.Class)
                parts.Add("abstract");
            if (typeSymbol.IsSealed && typeSymbol.TypeKind == TypeKind.Class)
                parts.Add("sealed");
            if (typeSymbol.IsReadOnly && typeSymbol.TypeKind == TypeKind.Struct)
                parts.Add("readonly");
        }

        parts.Add("partial");
        parts.Add(TypeKindKeyword(typeSymbol));
        parts.Add(FormatTypeName(typeSymbol));

        var declaration = string.Join(" ", parts);
        var constraints = FormatTypeParameterConstraints(typeSymbol);
        return string.IsNullOrEmpty(constraints) ? declaration : $"{declaration} {constraints}";
    }

    private static string AccessibilityToString(Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal"
        };

    private static string TypeKindKeyword(INamedTypeSymbol typeSymbol) =>
        typeSymbol.IsRecord
            ? typeSymbol.IsValueType ? "record struct" : "record class"
            : typeSymbol.TypeKind switch
            {
                TypeKind.Struct => "struct",
                TypeKind.Interface => "interface",
                _ => "class"
            };

    private static string FormatTypeName(INamedTypeSymbol typeSymbol) =>
        typeSymbol.TypeParameters.Length > 0
            ? $"{EscapeIdentifier(typeSymbol.Name)}<{string.Join(", ", typeSymbol.TypeParameters.Select(FormatTypeParameterName))}>"
            : EscapeIdentifier(typeSymbol.Name);

    private static string FormatTypeParameterName(ITypeParameterSymbol typeParameter)
    {
        var variance = typeParameter.Variance switch
        {
            VarianceKind.In => "in ",
            VarianceKind.Out => "out ",
            _ => string.Empty
        };

        return variance + EscapeIdentifier(typeParameter.Name);
    }

    private static string FormatTypeParameterConstraints(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeParameters.Length == 0)
            return string.Empty;

        var clauses = new List<string>();
        foreach (var typeParameter in typeSymbol.TypeParameters)
        {
            var constraints = new List<string>();

            if (typeParameter.HasUnmanagedTypeConstraint)
                constraints.Add("unmanaged");
            else if (typeParameter.HasValueTypeConstraint)
                constraints.Add("struct");
            else if (typeParameter.HasReferenceTypeConstraint)
                constraints.Add(typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            else if (typeParameter.HasNotNullConstraint)
                constraints.Add("notnull");

            constraints.AddRange(typeParameter.ConstraintTypes.Select(static constraintType =>
                constraintType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

            if (typeParameter.HasConstructorConstraint
                && !typeParameter.HasValueTypeConstraint
                && !typeParameter.HasUnmanagedTypeConstraint)
                constraints.Add("new()");

            if (constraints.Count > 0)
                clauses.Add($"where {EscapeIdentifier(typeParameter.Name)} : {string.Join(", ", constraints)}");
        }

        return string.Join(" ", clauses);
    }

    private static string EscapeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private static string BuildNestingOpen(RequiredPartialClassInfo info) =>
        info.NestingParents.Length == 0
            ? string.Empty
            : string.Join("\n", info.NestingParents.Select(parent => $"{parent}\n{{")) + "\n";

    private static string BuildNestingClose(RequiredPartialClassInfo info) =>
        info.NestingParents.Length == 0
            ? string.Empty
            : string.Join("\n", info.NestingParents.Select(_ => "}").Reverse()) + "\n";

    /// <summary>
    /// Formats a double value as a string suitable for use as a C# decimal literal (without 'm' suffix).
    /// Returns null if the value exceeds the decimal range.
    /// </summary>
    private static string? FormatDecimalLiteral(double value)
    {
        try
        {
            var decimalValue = (decimal)value;
            return decimalValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Fast syntax-only filter to identify potential value object class declarations.
    /// </summary>
    /// <param name="node">The syntax node to examine.</param>
    /// <returns>
    /// <c>true</c> if the node is a partial class inheriting from a supported Required* base type;
    /// otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This predicate is called for every syntax node in the compilation, so it must be fast.
    /// It uses only syntactic analysis (no semantic model) to quickly filter candidates.
    /// </para>
    /// <para>
    /// The method checks:
    /// <list type="number">
    /// <item>Is it a class declaration?</item>
    /// <item>Does it have a base type list?</item>
    /// <item>Is the first base type named "RequiredGuid" or "RequiredString"?</item>
    /// </list>
    /// </para>
    /// <para>
    /// Note: This is a syntactic check only. Semantic validation happens later in <see cref="GetTypesToGenerate"/>.
    /// </para>
    /// </remarks>
    private static bool IsSyntaxTargetForGeneration(SyntaxNode node)
    {
        if (node is ClassDeclarationSyntax c && c.BaseList != null)
        {
            var baseType = c.BaseList.Types.FirstOrDefault();
            var nameOfFirstBaseType = baseType?.Type.ToString();

            // Support both old names and new generic names
            // RequiredString<ClassName> or RequiredString (for backwards compat during migration)
            if (nameOfFirstBaseType != null && nameOfFirstBaseType.StartsWith("RequiredString", StringComparison.Ordinal))
                return true;
            if (nameOfFirstBaseType != null && nameOfFirstBaseType.StartsWith("RequiredGuid", StringComparison.Ordinal))
                return true;
            if (nameOfFirstBaseType != null && nameOfFirstBaseType.StartsWith("RequiredInt", StringComparison.Ordinal))
                return true;
            if (nameOfFirstBaseType != null && nameOfFirstBaseType.StartsWith("RequiredDecimal", StringComparison.Ordinal))
                return true;
            if (nameOfFirstBaseType != null && nameOfFirstBaseType.StartsWith("RequiredLong", StringComparison.Ordinal))
                return true;
            if (nameOfFirstBaseType != null && nameOfFirstBaseType.StartsWith("RequiredBool", StringComparison.Ordinal))
                return true;
            if (nameOfFirstBaseType != null && nameOfFirstBaseType.StartsWith("RequiredDateTime", StringComparison.Ordinal))
                return true;
            if (nameOfFirstBaseType != null && nameOfFirstBaseType.StartsWith("RequiredEnum", StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
