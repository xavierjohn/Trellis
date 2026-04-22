namespace SourceGenerator;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
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
/// <item><c>RequiredGuid</c> — rejects <c>Guid.Empty</c>; generates <c>NewUniqueV4()</c> and <c>NewUniqueV7()</c></item>
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
/// var id = CustomerId.NewUnique();
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
///             .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = "Customer Id cannot be empty." }})))
///             .Ensure(x =&gt; x != Guid.Empty, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = "Customer Id cannot be empty." }})))
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
    /// <item>Filters to only classes inheriting from RequiredGuid or RequiredString</item>
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
    /// <item>Determines which template to use based on base class (RequiredGuid vs RequiredString)</item>
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
                        messageFormat: "Class '{0}' inherits from unsupported base type '{1}'. Supported bases: RequiredGuid, RequiredString, RequiredInt, RequiredDecimal, RequiredLong, RequiredBool, RequiredDateTime, RequiredEnum.",
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

            // RequiredEnum has a completely different structure - handle separately
            if (g.ClassBase == "RequiredEnum")
            {
                context.AddSource($"{g.TypePath}.g.cs", GenerateEnumSource(g, nestedTypeOpen, nestedTypeClose));
                continue;
            }

            // Build up the source code
            // Note: The base class is already declared in the user's partial class.
            // We only generate the additional members and interfaces.
            var isFormattable = g.ClassBase is "RequiredInt" or "RequiredDecimal" or "RequiredLong" or "RequiredDateTime";
            var formattableInterface = isFormattable
                ? $", IFormattableScalarValue<{g.ClassName}, {classType}>"
                : "";

            var source = $@"// <auto-generated/>
    namespace {g.NameSpace};
    using System;
    using Trellis;
    using Trellis.Primitives;
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Serialization;

    #nullable enable
            {nestedTypeOpen}
    [JsonConverter(typeof(ParsableJsonConverter<{g.ClassName}>))]
    {g.Accessibility.ToCamelCase()} partial class {g.ClassName} : IScalarValue<{g.ClassName}, {classType}>{formattableInterface}, IParsable<{g.ClassName}>
    {{
        private {g.ClassName}({classType} value) : base(value)
        {{
        }}

        public static explicit operator {g.ClassName}({classType} {camelArg}) => Create({camelArg});

        public static {g.ClassName} Parse(string s, IFormatProvider? provider)
        {{
            var r = TryCreate(s, {(isFormattable ? "provider" : "null")});
            return r.Match(
                onSuccess: value => value,
                onFailure: error =>
                {{
                    var val = (Error.UnprocessableContent)error;
                    throw new FormatException(val.Fields.Items[0].Detail ?? val.Fields.Items[0].ReasonCode);
                }});
        }}

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
        }}";

            // Generate type-specific factory methods (TryCreate, Create, validation hooks)
            var methods = g.ClassBase switch
            {
                "RequiredGuid" => GenerateGuidMethods(g),
                "RequiredString" => GenerateStringMethods(g, context),
                "RequiredInt" => GenerateIntMethods(g, context),
                "RequiredDecimal" => GenerateDecimalMethods(g, context),
                "RequiredLong" => GenerateLongMethods(g, context),
                "RequiredBool" => GenerateBoolMethods(g),
                "RequiredDateTime" => GenerateDateTimeMethods(g),
                _ => null
            };

            if (methods is null) continue;

            source += methods;
            source += $@"
    }}
    {nestedTypeClose}";

            context.AddSource($"{g.TypePath}.g.cs", source);
        }
    }

    private static string GenerateEnumSource(RequiredPartialClassInfo g, string nestedTypeOpen, string nestedTypeClose) =>
        $@"// <auto-generated/>
    namespace {g.NameSpace};
    using System;
    using Trellis;
    using Trellis.Primitives;
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Serialization;

    #nullable enable
    {nestedTypeOpen}
    [JsonConverter(typeof(RequiredEnumJsonConverter<{g.ClassName}>))]
    {g.Accessibility.ToCamelCase()} partial class {g.ClassName} : IScalarValue<{g.ClassName}, string>, IParsable<{g.ClassName}>
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

        public static {g.ClassName} Parse(string s, IFormatProvider? provider)
        {{
            var r = TryCreate(s, null);
            return r.Match(
                onSuccess: value => value,
                onFailure: error =>
                {{
                    var val = (Error.UnprocessableContent)error;
                    throw new FormatException(val.Fields.Items[0].Detail ?? val.Fields.Items[0].ReasonCode);
                }});
        }}

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

    private static string GenerateGuidMethods(RequiredPartialClassInfo g) =>
        $@"

        /// <summary>
        /// Optional validation hook. Implement this partial method to add custom validation.
        /// Called after built-in validations (empty GUID check) pass.
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
        /// Creates a validated instance from a Guid.
        /// Required by IScalarValue interface for model binding and JSON deserialization.
        /// </summary>
        /// <param name=""value"">The Guid value to validate.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(Guid value, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            if (value == Guid.Empty)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(Guid? requiredGuidOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = requiredGuidOrNothing
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => x != Guid.Empty, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(guid => new {g.ClassName}(guid));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            Guid parsedGuid = Guid.Empty;
            var validated = stringOrNull
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => Guid.TryParse(x, out parsedGuid), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Guid should contain 32 digits with 4 dashes (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)"" }})))
                .Ensure(_ => parsedGuid != Guid.Empty, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedGuid, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
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

    private static string? GenerateStringMethods(RequiredPartialClassInfo g, SourceProductionContext context)
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
        return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {g.MinLength.Value} {"character" + (g.MinLength.Value == 1 ? "" : "s")}."" }})));";
        }

        if (g.MaxLength.HasValue)
        {
            lengthChecks += $@"
    if (normalized.Length > {g.MaxLength.Value})
        return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be {g.MaxLength.Value} {"character" + (g.MaxLength.Value == 1 ? "" : "s")} or fewer."" }})));";
        }

        return $@"

        /// <summary>
        /// Optional validation hook. Implement this partial method to add custom validation
        /// (e.g., regex patterns, format checks). Called after built-in validations pass.
        /// </summary>
        /// <param name=""value"">The validated, trimmed string value.</param>
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
            var notEmpty = value
                .EnsureNotNullOrWhiteSpace(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (!notEmpty.TryGetValue(out var notEmptyValue))
                return notEmpty.Map(str => new {g.ClassName}(str));
            var normalized = notEmptyValue.Trim();{lengthChecks}
            string? additionalError = null;
            ValidateAdditional(normalized, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
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

    private static string? GenerateIntMethods(RequiredPartialClassInfo g, SourceProductionContext context)
    {
        var result = "";
        var hasRange = g.RangeMin.HasValue && g.RangeMax.HasValue;
        var rangeMin = g.RangeMin.GetValueOrDefault();
        var rangeMax = g.RangeMax.GetValueOrDefault();

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
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            if (value < {rangeMin})
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {rangeMin}."" }})));
            if (value > {rangeMax})
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {rangeMax}."" }})));
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(int? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => x >= {rangeMin}, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {rangeMin}."" }})))
                .Ensure(x => x <= {rangeMax}, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {rangeMax}."" }})));
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            int parsedInt = 0;
            var validated = stringOrNull
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => int.TryParse(x, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsedInt), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid integer."" }})))
                .Ensure(_ => parsedInt >= {rangeMin}, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {rangeMin}."" }})))
                .Ensure(_ => parsedInt <= {rangeMax}, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {rangeMax}."" }})));
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedInt, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
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
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(int? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            int parsedInt = 0;
            var validated = stringOrNull
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => int.TryParse(x, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsedInt), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid integer."" }})));
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedInt, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
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
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, provider ?? System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid integer."" }})));
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

    private static string? GenerateDecimalMethods(RequiredPartialClassInfo g, SourceProductionContext context)
    {
        var result = "";
        var hasRange = g.RangeDoubleMin.HasValue && g.RangeDoubleMax.HasValue;
        // Use double values for fractional ranges, fall back to int values
        var rangeMinD = g.RangeDoubleMin ?? (double?)g.RangeMin;
        var rangeMaxD = g.RangeDoubleMax ?? (double?)g.RangeMax;
        hasRange = hasRange || (g.RangeMin.HasValue && g.RangeMax.HasValue);

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
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            if (value < {minStr}m)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {minStr}."" }})));
            if (value > {maxStr}m)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {maxStr}."" }})));
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(decimal? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => x >= {minStr}m, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {minStr}."" }})))
                .Ensure(x => x <= {maxStr}m, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {maxStr}."" }})));
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            decimal parsedDecimal = 0m;
            var validated = stringOrNull
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => decimal.TryParse(x, out parsedDecimal), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid decimal."" }})))
                .Ensure(_ => parsedDecimal >= {minStr}m, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {minStr}."" }})))
                .Ensure(_ => parsedDecimal <= {maxStr}m, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {maxStr}."" }})));
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedDecimal, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
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
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(decimal? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            decimal parsedDecimal = 0m;
            var validated = stringOrNull
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => decimal.TryParse(x, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out parsedDecimal), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid decimal."" }})));
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedDecimal, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
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
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (!decimal.TryParse(value, System.Globalization.NumberStyles.Number, provider ?? System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid decimal."" }})));
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

    private static string? GenerateLongMethods(RequiredPartialClassInfo g, SourceProductionContext context)
    {
        var result = "";
        var hasRange = g.RangeLongMin.HasValue && g.RangeLongMax.HasValue;
        var rangeLongMin = g.RangeLongMin.GetValueOrDefault();
        var rangeLongMax = g.RangeLongMax.GetValueOrDefault();

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
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            if (value < {rangeLongMin}L)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {rangeLongMin}."" }})));
            if (value > {rangeLongMax}L)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {rangeLongMax}."" }})));
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(long? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => x >= {rangeLongMin}L, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {rangeLongMin}."" }})))
                .Ensure(x => x <= {rangeLongMax}L, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {rangeLongMax}."" }})));
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            long parsedLong = 0;
            var validated = stringOrNull
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => long.TryParse(x, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsedLong), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid long."" }})))
                .Ensure(_ => parsedLong >= {rangeLongMin}L, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at least {rangeLongMin}."" }})))
                .Ensure(_ => parsedLong <= {rangeLongMax}L, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} must be at most {rangeLongMax}."" }})));
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedLong, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
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
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(long? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            long parsedLong = 0;
            var validated = stringOrNull
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => long.TryParse(x, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsedLong), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid long."" }})));
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedLong, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
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
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (!long.TryParse(value, System.Globalization.NumberStyles.Integer, provider ?? System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid long."" }})));
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

    private static string GenerateBoolMethods(RequiredPartialClassInfo g) =>
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
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(bool? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            bool parsedBool = false;
            var validated = stringOrNull
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => bool.TryParse(x, out parsedBool), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid boolean (true or false)."" }})));
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedBool, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
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

    private static string GenerateDateTimeMethods(RequiredPartialClassInfo g) =>
        $@"

        /// <summary>
        /// Optional validation hook. Implement this partial method to add custom validation.
        /// Called after built-in validations (null-check, MinValue check) pass.
        /// </summary>
        /// <param name=""value"">The validated DateTime value.</param>
        /// <param name=""fieldName"">The normalized field name for error messages.</param>
        /// <param name=""errorMessage"">Set to a non-null string to reject the value.</param>
        static partial void ValidateAdditional(DateTime value, string fieldName, ref string? errorMessage);

        /// <summary>
        /// Creates a validated instance from a DateTime.
        /// Required by IScalarValue interface for model binding and JSON deserialization.
        /// </summary>
        /// <param name=""value"">The DateTime value to validate. Must not be <see cref=""DateTime.MinValue""/>.</param>
        /// <param name=""fieldName"">Optional field name for validation error messages.</param>
        /// <returns>Success with the value object, or Failure with validation errors.</returns>
        public static Result<{g.ClassName}> TryCreate(DateTime value, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            if (value == DateTime.MinValue)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            string? additionalError = null;
            ValidateAdditional(value, field, ref additionalError);
            if (additionalError is not null)
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            return Result.Ok(new {g.ClassName}(value));
        }}

        public static Result<{g.ClassName}> TryCreate(DateTime? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            var validated = valueOrNothing
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => x != DateTime.MinValue, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (validated.TryGetValue(out var value))
            {{
                string? additionalError = null;
                ValidateAdditional(value, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
            }}
            return validated.Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            DateTime parsedDateTime = DateTime.MinValue;
            var validated = stringOrNull
                .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})))
                .Ensure(x => DateTime.TryParse(x, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out parsedDateTime), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid date/time."" }})))
                .Ensure(_ => parsedDateTime != DateTime.MinValue, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (validated.IsSuccess)
            {{
                string? additionalError = null;
                ValidateAdditional(parsedDateTime, field, ref additionalError);
                if (additionalError is not null)
                    return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = additionalError }})));
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
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""{g.ClassName.SplitPascalCase()} cannot be empty."" }})));
            if (!DateTime.TryParse(value, provider ?? System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                return Result.Fail<{g.ClassName}>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ""validation.error"") {{ Detail = ""Value must be a valid date/time."" }})));
            return TryCreate(parsed, fieldName);
        }}

        /// <summary>
        /// Creates a validated instance from a DateTime. Throws if validation fails.
        /// Use this for known-valid values in tests or with constants.
        /// </summary>
        /// <param name=""value"">The DateTime value to validate.</param>
        /// <returns>The validated value object.</returns>
        /// <exception cref=""InvalidOperationException"">Thrown when validation fails.</exception>
        public static new {g.ClassName} Create(DateTime value)
        {{
            var result = TryCreate(value, null);
            return result.Match(
                onSuccess: created => created,
                onFailure: error => throw new InvalidOperationException($""Failed to create {g.ClassName}: {{error.GetDisplayMessage()}}""));
        }}

        /// <summary>
        /// Creates a validated instance from a string by parsing it as a DateTime. Throws if validation or parsing fails.
        /// Use this for known-valid date/time strings in tests or with constants.
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
    /// <item>Base class name (RequiredGuid or RequiredString)</item>
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
            string @namespace = classSymbol.ContainingNamespace.ToDisplayString();
            // Extract just the base class name without type parameters (e.g., "RequiredGuid" from "RequiredGuid<EmployeeId>")
            string @base = classSymbol.BaseType?.Name ?? "unknown";
            string accessibility = classSymbol.DeclaredAccessibility.ToString();
            var nestingParents = GetNestingParents(classSymbol);
            var typePath = BuildTypePath(@namespace, classSymbol);

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

            classToGenerate.Add(new RequiredPartialClassInfo(@namespace, className, @base, accessibility, maxLength, minLength, rangeMin, rangeMax, rangeLongMin, rangeLongMax, rangeDoubleMin, rangeDoubleMax, nestingParents, typePath));
        }

        return classToGenerate;
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
        var kind = typeSymbol.IsRecord
            ? typeSymbol.IsValueType ? "record struct" : "record class"
            : typeSymbol.TypeKind switch
            {
                TypeKind.Struct => "struct",
                _ => "class"
            };

        return $"{typeSymbol.DeclaredAccessibility.ToString().ToCamelCase()} partial {kind} {typeSymbol.Name}";
    }

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
    /// <c>true</c> if the node is a partial class inheriting from RequiredGuid or RequiredString;
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