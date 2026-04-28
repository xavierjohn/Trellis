namespace Trellis;

using System.Diagnostics;
using System.Reflection;
using Trellis.Primitives;

/// <summary>
/// Provides OpenTelemetry activity tracing for Trellis primitive value object operations.
/// Enables monitoring and diagnostics of value object creation, validation, and parsing activities.
/// </summary>
/// <remarks>
/// <para>
/// This static class configures distributed tracing support for Trellis primitive value objects,
/// allowing you to observe value object operations in Application Insights, Jaeger, Zipkin,
/// or other OpenTelemetry-compatible observability platforms.
/// </para>
/// <para>
/// The <see cref="ActivitySource"/> enables tracing for:
/// <list type="bullet">
/// <item>Email address validation (EmailAddress.TryCreate)</item>
/// <item>Value object parsing and creation</item>
/// <item>Validation success and failure rates</item>
/// <item>Performance metrics for value object operations</item>
/// </list>
/// </para>
/// <para>
/// To enable tracing in your application, register the activity source with your
/// OpenTelemetry configuration.
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
///             .AddPrimitiveValueObjectInstrumentation()  // Adds Trellis primitive tracing
///             .AddAspNetCoreInstrumentation()
///             .AddHttpClientInstrumentation()
///             .AddConsoleExporter());
/// 
/// var app = builder.Build();
/// 
/// // Now EmailAddress.TryCreate and other primitive value object operations will be traced
/// app.MapPost("/users", (CreateUserRequest request) =>
///     EmailAddress.TryCreate(request.Email) // This creates a traced activity
///         .Bind(email => _userService.CreateUser(email))
///         .ToHttpResponse());
/// </code>
/// </example>
/// <example>
/// Viewing traces in Application Insights:
/// <code>
/// // Trace hierarchy example:
/// // POST /users
/// //   └─ EmailAddress.TryCreate
/// //      ├─ Status: Ok (if valid)
/// //      └─ Status: Error (if invalid)
/// //   └─ UserService.CreateUser
/// //      └─ Database operation
/// 
/// // Each activity includes:
/// // - Operation name: "EmailAddress.TryCreate"
/// // - Duration
/// // - Status (Ok/Error)
/// // - Parent/child relationships
/// </code>
/// </example>
/// <seealso cref="ActivitySource"/>
public static class PrimitiveValueObjectTrace
{
    /// <summary>
    /// Gets the assembly name for Trellis primitive value object tracing.
    /// Used for versioning and metadata in traces.
    /// </summary>
    internal static readonly AssemblyName AssemblyName = typeof(PrimitiveValueObjectTrace).Assembly.GetName();

    /// <summary>
    /// Gets the name of the activity source used for primitive value object tracing.
    /// Uses the existing activity source identifier for compatibility with current telemetry pipelines.
    /// </summary>
    /// <remarks>
    /// This name is used to identify traces from this library in observability platforms.
    /// Register this name when configuring OpenTelemetry tracing.
    /// </remarks>
    internal static readonly string ActivitySourceName = "Trellis.Primitives";

    /// <summary>
    /// Gets the version used for primitive value object tracing metadata.
    /// </summary>
    /// <remarks>
    /// The version is included in trace metadata to help correlate behavior with specific library versions.
    /// </remarks>
    internal static readonly Version Version = AssemblyName.Version!;

    private static readonly ActivitySource _defaultActivitySource = new(ActivitySourceName, Version.ToString());

    // Use AsyncLocal for test isolation - works across async boundaries and is thread-safe
    private static readonly AsyncLocal<ActivitySource?> _testActivitySource = new();

    /// <summary>
    /// Gets the <see cref="ActivitySource"/> for tracing primitive value object operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This activity source is used by value objects (like <see cref="EmailAddress"/>) to create
    /// tracing spans for operations such as validation and parsing.
    /// </para>
    /// <para>
    /// To enable tracing, add this source to your OpenTelemetry configuration:
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithTracing(b => b.AddPrimitiveValueObjectInstrumentation());
    /// </code>
    /// </para>
    /// <para>
    /// Activities created include:
    /// <list type="bullet">
    /// <item>Activity name: "{ValueObjectType}.{MethodName}" (e.g., "EmailAddress.TryCreate")</item>
    /// <item>Status: Ok for successful operations, Error for failures</item>
    /// <item>Duration: Time taken for the operation</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static ActivitySource ActivitySource => _testActivitySource.Value ?? _defaultActivitySource;

    /// <summary>
    /// Sets a test-specific ActivitySource for isolated testing.
    /// This allows tests to capture activities without interfering with each other.
    /// Uses AsyncLocal to ensure proper isolation even with async tests and parallel execution.
    /// </summary>
    /// <param name="testSource">The test-specific ActivitySource to use.</param>
    internal static void SetTestActivitySource(ActivitySource testSource) => _testActivitySource.Value = testSource;

    /// <summary>
    /// Resets the ActivitySource to the default production instance.
    /// Should be called in test cleanup (Dispose).
    /// </summary>
    internal static void ResetTestActivitySource() => _testActivitySource.Value = null;
}