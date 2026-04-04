namespace Trellis;

/// <summary>
/// Controls runtime behavior of <see cref="ResultDebugExtensions"/> methods
/// (<c>Debug</c>, <c>DebugDetailed</c>, <c>DebugWithStack</c>, <c>DebugOnSuccess</c>, <c>DebugOnFailure</c>).
/// </summary>
/// <remarks>
/// <para>
/// In DEBUG builds, <see cref="EnableDebugTracing"/> defaults to <c>true</c> so debug spans are emitted.
/// In RELEASE builds, the debug methods are compile-time no-ops regardless of this flag.
/// </para>
/// <para>
/// Set <see cref="EnableDebugTracing"/> to <c>false</c> in DEBUG builds to suppress debug span emission
/// at runtime — useful for integration tests or staging environments built with DEBUG configuration.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Disable debug tracing at runtime (DEBUG builds only)
/// ResultDebugSettings.EnableDebugTracing = false;
/// </code>
/// </example>
public static class ResultDebugSettings
{
    /// <summary>
    /// Gets or sets whether debug tracing is enabled at runtime.
    /// Default is <c>true</c> in DEBUG builds, <c>false</c> in RELEASE builds.
    /// Has no effect in RELEASE builds where debug methods are compile-time no-ops.
    /// </summary>
    public static bool EnableDebugTracing { get; set; }
#if DEBUG
        = true;
#endif
}
