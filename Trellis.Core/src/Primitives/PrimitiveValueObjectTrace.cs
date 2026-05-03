namespace Trellis;

using System.Diagnostics;
using System.Reflection;

/// <summary>
/// Provides OpenTelemetry activity tracing for Trellis primitive value object operations.
/// </summary>
public static class PrimitiveValueObjectTrace
{
    /// <summary>
    /// Gets the assembly name used for primitive value object trace metadata.
    /// </summary>
    internal static readonly AssemblyName AssemblyName = typeof(PrimitiveValueObjectTrace).Assembly.GetName();

    /// <summary>
    /// Gets the activity source name used by generated and built-in primitive value objects.
    /// </summary>
    internal static readonly string ActivitySourceName = "Trellis.Primitives";

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
