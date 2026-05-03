namespace Trellis;

using System.Diagnostics;
using System.Reflection;

/// <summary>
/// Provides OpenTelemetry activity tracing for Trellis primitive value object operations.
/// </summary>
/// <remarks>
/// The activity source is named <c>"Trellis.Primitives"</c> because it identifies the
/// brand of concrete primitive value objects (e.g. <c>EmailAddress</c>, <c>Money</c>,
/// <c>CountryCode</c>) that Trellis ships, not the hosting assembly. The version stamped
/// on the source is read from the assembly that physically contains this type — currently
/// <c>Trellis.Core</c>, since <c>Trellis.Primitives</c> only type-forwards
/// <see cref="PrimitiveValueObjectTrace"/>. This relies on <c>Trellis.Core</c> and
/// <c>Trellis.Primitives</c> shipping in lockstep from the same <c>version.json</c>; if
/// the two packages are ever decoupled in versioning, the stamped version will drift
/// from the <c>Trellis.Primitives</c> NuGet package version that consumers reference.
/// </remarks>
public static class PrimitiveValueObjectTrace
{
    /// <summary>
    /// Gets the assembly name used for primitive value object trace metadata.
    /// </summary>
    internal static readonly AssemblyName AssemblyName = typeof(PrimitiveValueObjectTrace).Assembly.GetName();

    /// <summary>
    /// Gets the activity source name used by generated and built-in primitive value objects.
    /// </summary>
    public static string ActivitySourceName => "Trellis.Primitives";

    /// <summary>
    /// Gets the version used for primitive value object trace metadata.
    /// </summary>
    internal static readonly Version Version = AssemblyName.Version!;

    private static readonly ActivitySource _defaultActivitySource = new(ActivitySourceName, Version.ToString());
    private static readonly AsyncLocal<ActivitySource?> _testActivitySource = new();

    /// <summary>
    /// Gets the <see cref="ActivitySource"/> for primitive value object creation, parsing, and validation operations.
    /// </summary>
    public static ActivitySource ActivitySource => _testActivitySource.Value ?? _defaultActivitySource;

    internal static void SetTestActivitySource(ActivitySource testSource) => _testActivitySource.Value = testSource;

    internal static void ResetTestActivitySource() => _testActivitySource.Value = null;
}
