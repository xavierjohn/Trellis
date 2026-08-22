namespace Trellis.ServiceDefaults.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

/// <summary>
/// Encodes the naming rule for OpenTelemetry registration helpers: a helper must be called
/// <c>AddTrellis{Segment}Instrumentation</c>, where <c>{Segment}</c> is exactly the text following
/// <c>"Trellis."</c> in the <see cref="System.Diagnostics.ActivitySource"/> or
/// <see cref="System.Diagnostics.Metrics.Meter"/> name it registers.
/// </summary>
/// <remarks>
/// <para>
/// The rule makes the method name derivable from the telemetry name, in both directions: a reader
/// who sees a span tagged <c>Trellis.Mediator</c> knows to call
/// <c>AddTrellisMediatorInstrumentation()</c>, and a reader who sees the call knows which source
/// appears in their backend. That mapping is the whole value, and it is what drifted — the four
/// helpers were previously spelled four different ways, one of which named a concept
/// (<c>PrimitiveValueObject</c>) that appeared nowhere in the emitted telemetry.
/// </para>
/// <para>
/// Deriving the expectation from the registered source, rather than comparing against a table of
/// approved names, is deliberate. A table would be satisfied by a new helper that a maintainer
/// remembered to add to it, which is exactly the memory that failed the first time. Here the
/// telemetry name is the single source of truth and the method name must follow it.
/// </para>
/// <para>
/// Discovery is a source scan of the whole repository rather than reflection alone, because
/// reflection sees only this assembly's reference closure — a new instrumentation helper in a
/// package this test does not reference would otherwise be silently exempt. The two views are
/// cross-checked below, so an unreferenced package fails the build with an actionable message
/// instead of quietly dropping out of enforcement.
/// </para>
/// </remarks>
public class InstrumentationNamingTests
{
    private static readonly Regex DeclarationPattern = new(
        @"public\s+static\s+(?<builder>TracerProviderBuilder|MeterProviderBuilder)\s+(?<name>\w+)\s*\(\s*this\s+",
        RegexOptions.Compiled);

    [Fact]
    public void Every_instrumentation_helper_is_named_for_the_source_it_registers()
    {
        var violations = DiscoverHelpers()
            .Select(helper => new { helper, Expected = ExpectedName(helper) })
            .Where(x => !string.Equals(x.helper.Method.Name, x.Expected, StringComparison.Ordinal))
            .Select(x => $"{x.helper.Method.Name} registers \"{x.helper.RegisteredName}\" so it must be called {x.Expected}")
            .ToList();

        violations.Should().BeEmpty(
            "an instrumentation helper must be named AddTrellis<Segment>Instrumentation for the "
            + "source or meter it registers, so the call and the telemetry name identify each other");
    }

    /// <summary>
    /// Asserts that every instrumentation helper declared anywhere in the repository is visible to
    /// this test, so the naming rule is enforced against all of them rather than a subset.
    /// </summary>
    /// <remarks>
    /// Guards the discovery mechanism itself. Reflection covers only this assembly's reference
    /// closure, so without this a new instrumentation helper in an unreferenced package would pass
    /// by never being examined — a green test that enforces nothing.
    /// </remarks>
    [Fact]
    public void Every_instrumentation_helper_in_the_repository_is_reachable_for_verification()
    {
        var reflected = DiscoverHelpers().Select(h => h.Method.Name).ToHashSet(StringComparer.Ordinal);

        var unreachable = ScanRepositoryForHelperNames().Except(reflected, StringComparer.Ordinal).ToList();

        unreachable.Should().BeEmpty(
            "every instrumentation helper must be verifiable by this test. Add a ProjectReference "
            + "from Trellis.ServiceDefaults.Tests to the package declaring {0}, so the naming rule "
            + "is enforced rather than skipped");
    }

    /// <summary>
    /// Asserts that every registered telemetry name is also reachable as a public string member, so
    /// a consumer wiring <c>AddSource</c>/<c>AddMeter</c> by hand can name it without hardcoding a
    /// literal.
    /// </summary>
    /// <remarks>
    /// The helper is the recommended path but not the only one: a consumer composing a source list
    /// manually, or filtering in a processor, needs the name as a value. Leaving one name
    /// internal while its three siblings are public is the kind of gap documentation papers over —
    /// six doc sites cited an internal <c>RopTrace.ActivitySourceName</c>, one of them in a
    /// copy-paste migration snippet that could not compile for the reader it was written for.
    /// </remarks>
    [Fact]
    public void Every_registered_telemetry_name_is_exposed_as_a_public_constant()
    {
        var exposed = ReferencedTrellisAssemblies()
            .Where(a => !a.GetName().Name!.EndsWith(".Tests", StringComparison.Ordinal))
            .SelectMany(PublicTelemetryNameValues)
            .ToHashSet(StringComparer.Ordinal);

        var hidden = DiscoverHelpers()
            .Select(h => h.RegisteredName)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !exposed.Contains(name))
            .ToList();

        hidden.Should().BeEmpty(
            "a consumer who wires AddSource or AddMeter by hand must be able to name the telemetry "
            + "without hardcoding a literal, so every registered name needs a public string member "
            + "holding it");
    }

    /// <summary>
    /// Every value held by a public static <c>ActivitySourceName</c> or <c>MeterName</c> string
    /// member on a public type — a <see langword="const"/> field, a <see langword="static"/>
    /// <see langword="readonly"/> field, or an expression-bodied property, which are the three
    /// shapes the telemetry names use between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matching on the member name as well as the value keeps the check from being satisfied by an
    /// unrelated constant that happens to hold the same string — an assembly name or package id, for
    /// instance — which would let the intended constant be deleted while the test stayed green.
    /// </para>
    /// <para>
    /// The exposing member deliberately does not have to live in the same assembly as the helper
    /// that registers it: <c>AddTrellisPrimitivesInstrumentation</c> ships in
    /// <c>Trellis.Primitives</c> while <c>PrimitiveValueObjectTrace</c> holds the name in
    /// <c>Trellis.Core</c>, and that split is legitimate.
    /// </para>
    /// <para>
    /// A <see langword="const"/> is read with <see cref="FieldInfo.GetRawConstantValue"/> rather
    /// than <see cref="FieldInfo.GetValue"/> so it is still found on an open generic type.
    /// <c>TracingBehavior&lt;TMessage, TResponse&gt;</c> holds the mediator source name and would
    /// otherwise be skipped, leaving a name unenforced because of where it happens to live.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> PublicTelemetryNameValues(Assembly assembly)
    {
        static bool IsTelemetryNameMember(string name) =>
            name is "ActivitySourceName" or "MeterName";

        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(string) || !IsTelemetryNameMember(field.Name))
                    continue;

                if (field.IsLiteral)
                {
                    if (field.GetRawConstantValue() is string literal)
                        yield return literal;
                }
                else if (!type.ContainsGenericParameters && field.GetValue(null) is string value)
                {
                    yield return value;
                }
            }

            if (type.ContainsGenericParameters)
                continue;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (property.PropertyType != typeof(string) || !IsTelemetryNameMember(property.Name))
                    continue;

                if (property.GetIndexParameters().Length == 0 && property.GetValue(null) is string value)
                    yield return value;
            }
        }
    }

    private static string ExpectedName(DiscoveredHelper helper)
    {
        const string prefix = "Trellis.";

        var segment = helper.RegisteredName.StartsWith(prefix, StringComparison.Ordinal)
            ? helper.RegisteredName[prefix.Length..]
            : helper.RegisteredName;

        return $"AddTrellis{segment.Replace(".", string.Empty, StringComparison.Ordinal)}Instrumentation";
    }

    /// <summary>
    /// Invokes each candidate helper against a recording builder, so the source it registers is
    /// observed rather than inferred from its name — the name is the thing under test.
    /// </summary>
    private static IEnumerable<DiscoveredHelper> DiscoverHelpers()
    {
        foreach (var method in ReferencedTrellisAssemblies().SelectMany(CandidateMethods))
        {
            var parameter = method.GetParameters()[0].ParameterType;

            if (parameter == typeof(TracerProviderBuilder))
            {
                var recorder = new RecordingTracerProviderBuilder();
                method.Invoke(null, [recorder]);
                foreach (var name in recorder.Sources)
                    yield return new DiscoveredHelper(method, name);
            }
            else if (parameter == typeof(MeterProviderBuilder))
            {
                var recorder = new RecordingMeterProviderBuilder();
                method.Invoke(null, [recorder]);
                foreach (var name in recorder.Meters)
                    yield return new DiscoveredHelper(method, name);
            }
        }
    }

    private static IEnumerable<MethodInfo> CandidateMethods(Assembly assembly) =>
        assembly.GetExportedTypes()
            .Where(t => t is { IsAbstract: true, IsSealed: true })
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), inherit: false))
            .Where(m => m.ReturnType == typeof(TracerProviderBuilder) || m.ReturnType == typeof(MeterProviderBuilder))
            .Where(m => m.GetParameters().Length == 1);

    /// <summary>
    /// Every Trellis assembly present in the test's output directory.
    /// </summary>
    /// <remarks>
    /// Loaded from the test's output directory rather than from
    /// <see cref="Assembly.GetReferencedAssemblies"/>: the compiler omits assembly references the
    /// test code does not actually use, so a package referenced solely to bring it under this rule
    /// would vanish from metadata and silently drop out of enforcement. A <c>ProjectReference</c>
    /// always copies the assembly to the output directory, which makes "add a ProjectReference" the
    /// complete fix the failure message promises.
    /// </remarks>
    private static Assembly[] ReferencedTrellisAssemblies() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "Trellis.*.dll")
            .Select(Assembly.LoadFrom)
            .Distinct()
            .ToArray();

    private static HashSet<string> ScanRepositoryForHelperNames()
    {
        var separator = Path.DirectorySeparatorChar;
        var names = new HashSet<string>(StringComparer.Ordinal);

        var sourceFiles = Directory
            .EnumerateFiles(RepositoryRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{separator}src{separator}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal));

        foreach (var path in sourceFiles)
            foreach (Match match in DeclarationPattern.Matches(File.ReadAllText(path)))
                names.Add(match.Groups["name"].Value);

        return names;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Trellis.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the test must be able to locate the repository root (Trellis.slnx)");
        return directory!.FullName;
    }

    private sealed record DiscoveredHelper(MethodInfo Method, string RegisteredName);

    private sealed class RecordingTracerProviderBuilder : TracerProviderBuilder
    {
        public List<string> Sources { get; } = [];

        public override TracerProviderBuilder AddInstrumentation<TInstrumentation>(
            Func<TInstrumentation> instrumentationFactory) => this;

        public override TracerProviderBuilder AddLegacySource(string operationName) => this;

        public override TracerProviderBuilder AddSource(params string[] names)
        {
            Sources.AddRange(names);
            return this;
        }
    }

    private sealed class RecordingMeterProviderBuilder : MeterProviderBuilder
    {
        public List<string> Meters { get; } = [];

        public override MeterProviderBuilder AddInstrumentation<TInstrumentation>(
            Func<TInstrumentation> instrumentationFactory) => this;

        public override MeterProviderBuilder AddMeter(params string[] names)
        {
            Meters.AddRange(names);
            return this;
        }
    }
}
