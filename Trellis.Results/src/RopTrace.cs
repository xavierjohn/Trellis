namespace Trellis;

using System.Diagnostics;
using System.Reflection;

/// <summary>
/// Provides OpenTelemetry activity tracing for Railway Oriented Programming operations.
/// Enables monitoring and diagnostics of Result operations including Bind, Map, Tap, Ensure, and other ROP methods.
/// </summary>
/// <remarks>
/// <para>
/// This static class configures distributed tracing support for the ROP library,
/// allowing you to observe Result operations in Application Insights, Jaeger, Zipkin,
/// or other OpenTelemetry-compatible observability platforms.
/// </para>
/// <para>
/// The <see cref="ActivitySource"/> enables tracing for:
/// <list type="bullet">
/// <item>Bind operations (chaining Result transformations)</item>
/// <item>Map operations (value transformations)</item>
/// <item>Tap operations (side effects)</item>
/// <item>Ensure operations (validation)</item>
/// <item>RecoverOnFailure operations (error recovery)</item>
/// <item>Combine operations (aggregating multiple Results)</item>
/// </list>
/// </para>
/// <para>
/// To enable tracing in your application, register the activity source with your
/// OpenTelemetry configuration using <see cref="ResultsTraceProviderBuilderExtensions.AddResultsInstrumentation"/>.
/// </para>
/// </remarks>
/// <example>
/// Enabling tracing in an ASP.NET Core application:
/// <code>
/// var builder = WebApplication.CreateBuilder(args);
/// 
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(tracerProviderBuilder =>
///         tracerProviderBuilder
///             .AddResultsInstrumentation()  // Adds ROP activity source
///             .AddAspNetCoreInstrumentation()
///             .AddHttpClientInstrumentation()
///             .AddConsoleExporter());
/// 
/// var app = builder.Build();
/// 
/// // Now Bind, Map, Tap, Ensure operations will be traced
/// app.MapPost("/users", (CreateUserRequest request) =>
///     EmailAddress.TryCreate(request.Email)
///         .Bind(email => User.Create(email))  // Traced activity
///         .Tap(user => _logger.LogInformation("Created user"))  // Traced activity
///         .ToHttpResult());
/// </code>
/// </example>
/// <seealso cref="ResultsTraceProviderBuilderExtensions"/>
internal static class RopTrace
{
    /// <summary>
    /// Gets the assembly name of the ROP library.
    /// Used for versioning and metadata in traces.
    /// </summary>
    internal static readonly AssemblyName AssemblyName = typeof(RopTrace).Assembly.GetName();

    /// <summary>
    /// Gets the name of the activity source used for ROP tracing.
    /// Value: "Trellis.Results"
    /// </summary>
    /// <remarks>
    /// This name is used to identify traces from this library in observability platforms.
    /// Register this name when configuring OpenTelemetry tracing using
    /// <see cref="ResultsTraceProviderBuilderExtensions.AddResultsInstrumentation"/>.
    /// </remarks>
    internal static readonly string ActivitySourceName = "Trellis.Results";

    /// <summary>
    /// Gets the version of the ROP library.
    /// </summary>
    /// <remarks>
    /// The version is included in trace metadata to help correlate behavior with specific library versions.
    /// </remarks>
    internal static readonly Version Version = AssemblyName.Version ?? new Version(0, 0, 0);

    private static readonly ActivitySource DefaultActivitySource = new(ActivitySourceName, Version.ToString());

    // Use AsyncLocal for test isolation - works across async boundaries and is thread-safe
    private static readonly AsyncLocal<ActivitySource?> _testActivitySource = new();

    /// <summary>
    /// Gets the <see cref="System.Diagnostics.ActivitySource"/> for tracing ROP operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This activity source is used by ROP extension methods (Bind, Map, Tap, Ensure, etc.) to create
    /// tracing spans for operations.
    /// </para>
    /// <para>
    /// To enable tracing, add this source to your OpenTelemetry configuration:
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithTracing(b => b.AddResultsInstrumentation());
    /// </code>
    /// </para>
    /// <para>
    /// Activities created include:
    /// <list type="bullet">
    /// <item>Activity name: Operation name (e.g., "Bind", "Map", "Tap", "Ensure")</item>
    /// <item>Status: Ok for successful operations, Error for failures</item>
    /// <item>Tags: Error codes and details when applicable</item>
    /// </list>
    /// </para>
    /// </remarks>
    internal static ActivitySource ActivitySource =>
        _testActivitySource.Value ?? DefaultActivitySource;

    /// <summary>
    /// Sets a custom ActivitySource for testing purposes.
    /// This allows tests to have complete isolation from other tests.
    /// Uses AsyncLocal to ensure proper isolation even with async tests and parallel execution.
    /// </summary>
    /// <param name="source">The test-specific ActivitySource to use.</param>
    internal static void SetTestActivitySource(ActivitySource source) => _testActivitySource.Value = source;

    /// <summary>
    /// Resets the ActivitySource to the default production source.
    /// Should be called in test cleanup/dispose.
    /// </summary>
    internal static void ResetTestActivitySource() => _testActivitySource.Value = null;
}