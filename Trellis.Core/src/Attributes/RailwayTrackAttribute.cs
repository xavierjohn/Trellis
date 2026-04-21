namespace Trellis;

/// <summary>
/// Indicates which railway track an operation executes on.
/// This metadata can be used by IDE extensions, analyzers, and documentation generators.
/// </summary>
/// <remarks>
/// <para>
/// Railway-Oriented Programming uses a railway track metaphor where operations flow along
/// either a success track or a failure track. This attribute makes the track behavior
/// explicit and machine-readable.
/// </para>
/// <para>
/// Track Behaviors:
/// <list type="bullet">
/// <item><see cref="TrackBehavior.Success"/> - Only executes when Result is successful</item>
/// <item><see cref="TrackBehavior.Failure"/> - Only executes when Result has failed</item>
/// <item><see cref="TrackBehavior.Both"/> - Executes on both success and failure tracks</item>
/// <item><see cref="TrackBehavior.Terminal"/> - Handles both tracks and exits the railway</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [RailwayTrack(TrackBehavior.Success)]
/// public static Result&lt;TOut&gt; Bind&lt;T, TOut&gt;(this Result&lt;T&gt; result, Func&lt;T, Result&lt;TOut&gt;&gt; func)
/// 
/// [RailwayTrack(TrackBehavior.Failure)]
/// public static Result&lt;T&gt; TapOnFailure&lt;T&gt;(this Result&lt;T&gt; result, Action&lt;Error&gt; action)
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RailwayTrackAttribute : Attribute
{
    /// <summary>
    /// Gets the track behavior of the annotated operation.
    /// </summary>
    public TrackBehavior Track { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RailwayTrackAttribute"/> class.
    /// </summary>
    /// <param name="track">The track behavior of the operation.</param>
    public RailwayTrackAttribute(TrackBehavior track) => Track = track;
}

/// <summary>
/// Defines which railway track an operation runs on in Railway-Oriented Programming.
/// </summary>
/// <remarks>
/// <para>
/// In Railway-Oriented Programming, operations flow along two tracks:
/// <list type="bullet">
/// <item><b>Success Track</b> - Operations continue when everything works correctly</item>
/// <item><b>Failure Track</b> - Errors are captured and propagated automatically</item>
/// </list>
/// </para>
/// <para>
/// Understanding track behavior is essential for composing railway-oriented pipelines.
/// This enum makes the behavior explicit and enables tooling support.
/// </para>
/// </remarks>
public enum TrackBehavior
{
    /// <summary>
    /// Operation only executes when Result is successful.
    /// Skipped if the Result is a failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Success track operations include:
    /// <list type="bullet">
    /// <item><c>Bind</c> - Chain operations that can fail</item>
    /// <item><c>Map</c> - Transform success values</item>
    /// <item><c>Tap</c> - Execute side effects on success</item>
    /// <item><c>Ensure</c> - Validate conditions (can switch to failure)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// result.Bind(user => GetOrders(user))  // Only runs if result is success
    ///       .Map(orders => orders.Count)     // Only runs if Bind succeeded
    ///       .Tap(count => Log(count));       // Only runs if Map succeeded
    /// </code>
    /// </para>
    /// </remarks>
    Success,

    /// <summary>
    /// Operation only executes when Result is a failure.
    /// Skipped if the Result is successful.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Failure track operations include:
    /// <list type="bullet">
    /// <item><c>TapOnFailure</c> - Execute side effects on errors</item>
    /// <item><c>MapOnFailure</c> - Transform errors</item>
    /// <item><c>RecoverOnFailure</c> - Attempt recovery from errors</item>
    /// </list>
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// result.TapOnFailure(err => LogError(err))      // Only runs if result failed
    ///       .MapOnFailure(err => AddContext(err))     // Only runs if still failed
    ///       .RecoverOnFailure(() => GetDefault());    // Attempts recovery if failed
    /// </code>
    /// </para>
    /// </remarks>
    Failure,

    /// <summary>
    /// Operation executes on both success and failure tracks.
    /// Processes the Result regardless of its state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Universal operations include:
    /// <list type="bullet">
    /// <item><c>Combine</c> - Merge multiple results (aggregates both successes and failures)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// firstName.Combine(lastName)  // Combines results regardless of individual success/failure
    ///          .Combine(email);     // Aggregates all errors if any fail
    /// </code>
    /// </para>
    /// </remarks>
    Both,

    /// <summary>
    /// Terminal operation that handles both tracks and exits the railway.
    /// Unwraps the Result and produces a final value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Terminal operations include:
    /// <list type="bullet">
    /// <item><c>Match</c> - Pattern match on success/failure</item>
    /// <item><c>MatchError</c> - Pattern match on specific error types</item>
    /// </list>
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// result.Match(
    ///     onSuccess: user => Ok(user),
    ///     onFailure: error => BadRequest(error.Detail)
    /// );  // Exits the railway and returns final value
    /// </code>
    /// </para>
    /// </remarks>
    Terminal
}