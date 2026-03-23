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
/// <c>RequiredDecimal</c>, or <c>RequiredEnum</c>.
/// </summary>
/// <remarks>
/// <para>
/// This incremental source generator analyzes partial class declarations and generates complementary code
/// that provides a complete, production-ready value object implementation. It eliminates boilerplate while
/// maintaining type safety and validation consistency.
/// </para>
/// <para>
/// For each partial class inheriting from <c>RequiredGuid</c>, generates:
/// <list type="bullet">
/// <item><c>IScalarValue&lt;TSelf, Guid&gt;</c> - Interface for ASP.NET Core automatic validation</item>
/// <item><c>NewUniqueV4()</c> - Creates a new instance with a unique Version 4 (random) GUID</item>
/// <item><c>NewUniqueV7()</c> - Creates a new instance with a unique Version 7 (time-ordered) GUID</item>
/// <item><c>TryCreate(Guid)</c> - Creates from non-nullable GUID (required by IScalarValue)</item>
/// <item><c>TryCreate(Guid?)</c> - Creates from nullable GUID with empty validation</item>
/// <item><c>TryCreate(string?)</c> - Parses from string with format and empty validation</item>
/// <item><c>Parse(string, IFormatProvider?)</c> - IParsable implementation</item>
/// <item><c>TryParse(...)</c> - IParsable try-parse pattern</item>
/// <item>Private constructor calling base class</item>
/// <item>JSON converter attribute for serialization</item>
/// <item>Explicit cast operator from GUID</item>
/// <item>OpenTelemetry activity tracing</item>
/// </list>
/// </para>
/// <para>
/// For each partial class inheriting from <c>RequiredString</c>, generates:
/// <list type="bullet">
/// <item><c>IScalarValue&lt;TSelf, string&gt;</c> - Interface for ASP.NET Core automatic validation</item>
/// <item><c>TryCreate(string)</c> - Creates from non-nullable string (required by IScalarValue)</item>
/// <item><c>TryCreate(string?)</c> - Creates from nullable string with null/empty/whitespace validation</item>
/// <item><c>Parse(string, IFormatProvider?)</c> - IParsable implementation</item>
/// <item><c>TryParse(...)</c> - IParsable try-parse pattern</item>
/// <item>Private constructor calling base class</item>
/// <item>JSON converter attribute for serialization</item>
/// <item>Explicit cast operator from string</item>
/// <item>OpenTelemetry activity tracing</item>
/// </list>
/// </para>
/// <para>
/// For each partial class inheriting from <c>RequiredEnum</c>, generates:
/// <list type="bullet">
/// <item><c>IScalarValue&lt;TSelf, string&gt;</c> - Interface for ASP.NET Core automatic validation</item>
/// <item><c>TryCreate(string)</c> - Creates by looking up enum member by symbolic value (delegates to TryFromName)</item>
/// <item><c>TryCreate(string?, string?)</c> - Creates with custom field name for validation errors</item>
/// <item><c>Parse(string, IFormatProvider?)</c> - IParsable implementation</item>
/// <item><c>TryParse(...)</c> - IParsable try-parse pattern</item>
/// <item>JSON converter attribute using <c>RequiredEnumJsonConverter&lt;T&gt;</c></item>
/// <item>OpenTelemetry activity tracing</item>
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
///             .ToResult(Error.Validation("Customer Id cannot be empty.", field))
///             .Ensure(x =&gt; x != Guid.Empty, Error.Validation("Customer Id cannot be empty.", field))
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
public class RequiredPartialClassGenerator : IIncrementalGenerator
{
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
                "RequiredEnum" => "enum",
                _ => null
            };

            // Skip unsupported base types and emit a diagnostic to inform the user
            if (classType is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        id: "TRLSGEN001",
                        title: "Unsupported base type for RequiredPartialClassGenerator",
                        messageFormat: "Class '{0}' inherits from unsupported base type '{1}'. Supported bases: RequiredGuid, RequiredString, RequiredInt, RequiredDecimal, RequiredEnum.",
                        category: "SourceGenerator",
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
            // Note: IScalarValue<TSelf, string> must be implemented on each derived class (not the base)
            // because it has a static abstract TryCreate method that abstract classes cannot satisfy.
            // The RequiredEnum base class has the constraint `where TSelf : IScalarValue<TSelf, string>`
            // to ensure all derived types implement the interface.
            if (g.ClassBase == "RequiredEnum")
            {
                var enumSource = $@"// <auto-generated/>
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
            if (r.IsFailure)
            {{
                var val = (ValidationError)r.Error;
                throw new FormatException(val.FieldErrors[0].Details[0]);
            }}
            return r.Value;
        }}

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out {g.ClassName} result)
        {{
            var r = TryCreate(s, null);
            if (r.IsFailure)
            {{
                result = default;
                return false;
            }}

            result = r.Value;
            return true;
        }}
    }}
    {nestedTypeClose}";
                context.AddSource($"{g.TypePath}.g.cs", enumSource);
                continue;
            }

            // Build up the source code
            // Note: The base class is already declared in the user's partial class.
            // We only generate the additional members and interfaces.
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
    {g.Accessibility.ToCamelCase()} partial class {g.ClassName} : IScalarValue<{g.ClassName}, {classType}>, IParsable<{g.ClassName}>
    {{
        private {g.ClassName}({classType} value) : base(value)
        {{
        }}

        public static explicit operator {g.ClassName}({classType} {camelArg}) => Create({camelArg});

        public static {g.ClassName} Parse(string s, IFormatProvider? provider)
        {{
            var r = TryCreate(s, null);
            if (r.IsFailure)
            {{
                var val = (ValidationError)r.Error;
                throw new FormatException(val.FieldErrors[0].Details[0]);
            }}
            return r.Value;
        }}

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out {g.ClassName} result)
        {{
            var r = TryCreate(s, null);
            if (r.IsFailure)
            {{
                result = default;
                return false;
            }}

            result = r.Value;
            return true;
        }}";

            if (g.ClassBase == "RequiredGuid")
            {
                source += $@"

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
                return Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be empty."", field);
            return new {g.ClassName}(value);
        }}

        public static Result<{g.ClassName}> TryCreate(Guid? requiredGuidOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            return requiredGuidOrNothing
                .ToResult(Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be empty."", field))
                .Ensure(x => x != Guid.Empty, Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be empty."", field))
                .Map(guid => new {g.ClassName}(guid));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            Guid parsedGuid = Guid.Empty;
            return stringOrNull
                .ToResult(Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be empty."", field))
                .Ensure(x => Guid.TryParse(x, out parsedGuid), Error.Validation(""Guid should contain 32 digits with 4 dashes (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)"", field))
                .Ensure(_ => parsedGuid != Guid.Empty, Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be empty."", field))
                .Map(guid => new {g.ClassName}(parsedGuid));
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
            if (result.IsFailure)
                throw new InvalidOperationException($""Failed to create {g.ClassName}: {{result.Error.Detail}}"");
            return result.Value;
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
            if (result.IsFailure)
                throw new InvalidOperationException($""Failed to create {g.ClassName}: {{result.Error.Detail}}"");
            return result.Value;
        }}
    }}
    {nestedTypeClose}";
            }

            if (g.ClassBase == "RequiredString")
            {
                // Validate [StringLength] constraints are consistent
                if (g.MinLength.HasValue && g.MaxLength.HasValue && g.MinLength.Value > g.MaxLength.Value)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            id: "TRLSGEN002",
                            title: "StringLength MinimumLength exceeds MaximumLength",
                            messageFormat: "Class '{0}' has [StringLength({1}, MinimumLength = {2})] where MinimumLength exceeds MaximumLength. No value can satisfy both constraints.",
                            category: "SourceGenerator",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        location: null,
                        g.ClassName,
                        g.MaxLength.Value,
                        g.MinLength.Value));
                    continue;
                }

                // Build length validation Ensure calls if [StringLength] is applied
                var lengthEnsures = "";

                if (g.MinLength.HasValue)
                {
                    lengthEnsures += $@"
                .Ensure(str => str.Length >= {g.MinLength.Value}, Error.Validation(""{g.ClassName.SplitPascalCase()} must be at least {g.MinLength.Value} {"character" + (g.MinLength.Value == 1 ? "" : "s")}."", field))";
                }

                if (g.MaxLength.HasValue)
                {
                    lengthEnsures += $@"
                .Ensure(str => str.Length <= {g.MaxLength.Value}, Error.Validation(""{g.ClassName.SplitPascalCase()} must be {g.MaxLength.Value} {"character" + (g.MaxLength.Value == 1 ? "" : "s")} or fewer."", field))";
                }

                source += $@"

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
            return value
                .EnsureNotNullOrWhiteSpace(Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be empty."", field)){lengthEnsures}
                .Map(str => new {g.ClassName}(str.Trim()));
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
            if (result.IsFailure)
                throw new InvalidOperationException($""Failed to create {g.ClassName}: {{result.Error.Detail}}"");
            return result.Value;
        }}
    }}
    {nestedTypeClose}";
            }

            if (g.ClassBase == "RequiredInt")
            {
                var hasRange = g.RangeMin.HasValue && g.RangeMax.HasValue;
                var rangeMin = g.RangeMin.GetValueOrDefault();
                var rangeMax = g.RangeMax.GetValueOrDefault();

                // Validate [Range] constraints are consistent
                if (hasRange && rangeMin > rangeMax)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            id: "TRLSGEN003",
                            title: "Range Minimum exceeds Maximum",
                            messageFormat: "Class '{0}' has [Range({1}, {2})] where Minimum exceeds Maximum. No value can satisfy both constraints.",
                            category: "SourceGenerator",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        location: null,
                        g.ClassName,
                        rangeMin,
                        rangeMax));
                    continue;
                }

                if (hasRange)
                {
                    // Range-validated TryCreate overloads
                    source += $@"

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
                return Error.Validation(""{g.ClassName.SplitPascalCase()} must be at least {rangeMin}."", field);
            if (value > {rangeMax})
                return Error.Validation(""{g.ClassName.SplitPascalCase()} must be at most {rangeMax}."", field);
            return new {g.ClassName}(value);
        }}

        public static Result<{g.ClassName}> TryCreate(int? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            return valueOrNothing
                .ToResult(Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be empty."", field))
                .Ensure(x => x >= {rangeMin}, Error.Validation(""{g.ClassName.SplitPascalCase()} must be at least {rangeMin}."", field))
                .Ensure(x => x <= {rangeMax}, Error.Validation(""{g.ClassName.SplitPascalCase()} must be at most {rangeMax}."", field))
                .Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            int parsedInt = 0;
            return stringOrNull
                .ToResult(Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be empty."", field))
                .Ensure(x => int.TryParse(x, out parsedInt), Error.Validation(""Value must be a valid integer."", field))
                .Ensure(_ => parsedInt >= {rangeMin}, Error.Validation(""{g.ClassName.SplitPascalCase()} must be at least {rangeMin}."", field))
                .Ensure(_ => parsedInt <= {rangeMax}, Error.Validation(""{g.ClassName.SplitPascalCase()} must be at most {rangeMax}."", field))
                .Map(_ => new {g.ClassName}(parsedInt));
        }}";
                }
                else
                {
                    // Default zero-check TryCreate overloads
                    source += $@"

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
            if (value == 0)
                return Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be zero."", field);
            return new {g.ClassName}(value);
        }}

        public static Result<{g.ClassName}> TryCreate(int? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            return valueOrNothing
                .ToResult(Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be empty."", field))
                .Ensure(x => x != 0, Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be zero."", field))
                .Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            int parsedInt = 0;
            return stringOrNull
                .ToResult(Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be empty."", field))
                .Ensure(x => int.TryParse(x, out parsedInt), Error.Validation(""Value must be a valid integer."", field))
                .Ensure(_ => parsedInt != 0, Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be zero."", field))
                .Map(_ => new {g.ClassName}(parsedInt));
        }}";
                }

                // Create and Parse are the same regardless of [Range]
                source += $@"

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
            if (result.IsFailure)
                throw new InvalidOperationException($""Failed to create {g.ClassName}: {{result.Error.Detail}}"");
            return result.Value;
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
            if (result.IsFailure)
                throw new InvalidOperationException($""Failed to create {g.ClassName}: {{result.Error.Detail}}"");
            return result.Value;
        }}
    }}
    {nestedTypeClose}";
            }

            if (g.ClassBase == "RequiredDecimal")
            {
                source += $@"

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
            if (value == 0m)
                return Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be zero."", field);
            return new {g.ClassName}(value);
        }}

        public static Result<{g.ClassName}> TryCreate(decimal? valueOrNothing, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            return valueOrNothing
                .ToResult(Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be empty."", field))
                .Ensure(x => x != 0m, Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be zero."", field))
                .Map(val => new {g.ClassName}(val));
        }}

        public static Result<{g.ClassName}> TryCreate(string? stringOrNull, string? fieldName = null)
        {{
            using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(""{g.ClassName}.TryCreate"");
            var field = fieldName.NormalizeFieldName(""{g.ClassName.ToCamelCase()}"");
            decimal parsedDecimal = 0m;
            return stringOrNull
                .ToResult(Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be empty."", field))
                .Ensure(x => decimal.TryParse(x, out parsedDecimal), Error.Validation(""Value must be a valid decimal."", field))
                .Ensure(_ => parsedDecimal != 0m, Error.Validation(""{g.ClassName.SplitPascalCase()} cannot be zero."", field))
                .Map(_ => new {g.ClassName}(parsedDecimal));
        }}

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
            if (result.IsFailure)
                throw new InvalidOperationException($""Failed to create {g.ClassName}: {{result.Error.Detail}}"");
            return result.Value;
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
            if (result.IsFailure)
                throw new InvalidOperationException($""Failed to create {g.ClassName}: {{result.Error.Detail}}"");
            return result.Value;
        }}
    }}
    {nestedTypeClose}";
            }

            context.AddSource($"{g.TypePath}.g.cs", source);
        }
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

            // Read [Range] attribute for RequiredInt types
            int? rangeMin = null;
            int? rangeMax = null;
            if (@base == "RequiredInt")
            {
                foreach (var attr in classSymbol.GetAttributes())
                {
                    if (attr.AttributeClass?.Name == "RangeAttribute"
                        && attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Trellis")
                    {
                        // Constructor: RangeAttribute(int minimum, int maximum)
                        if (attr.ConstructorArguments.Length >= 2)
                        {
                            if (attr.ConstructorArguments[0].Value is int min) rangeMin = min;
                            if (attr.ConstructorArguments[1].Value is int max) rangeMax = max;
                        }
                    }
                }
            }

            classToGenerate.Add(new RequiredPartialClassInfo(@namespace, className, @base, accessibility, maxLength, minLength, rangeMin, rangeMax, nestingParents, typePath));
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
            if (nameOfFirstBaseType != null && nameOfFirstBaseType.StartsWith("RequiredEnum", StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
