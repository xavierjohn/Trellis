namespace Trellis.Core.Tests.Helpers;

using System.Diagnostics;
using Trellis;

/// <summary>
/// Helper class for testing Activity tracing with complete isolation between tests.
/// Creates a unique ActivitySource per test instance to prevent cross-test contamination.
/// </summary>
public sealed class ActivityTestHelper : IDisposable
{
    private readonly ActivitySource _testActivitySource;
    private readonly ActivityListener _listener;
    private readonly List<Activity> _capturedActivities = new();
    private readonly object _lock = new();
    private bool _disposed;

    public ActivityTestHelper()
    {
        // Create a unique ActivitySource for this test
        _testActivitySource = new ActivitySource($"Test-ROP-{Guid.NewGuid():N}");

        // Configure the listener to capture activities from our test source
        _listener = new ActivityListener
        {
            // Always listen to our test-specific source for isolation
            ShouldListenTo = source => source == _testActivitySource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_lock)
                {
                    _capturedActivities.Add(activity);
                }
            }
        };

        ActivitySource.AddActivityListener(_listener);

        // Inject our test source into RopTrace (works in both DEBUG and RELEASE)
        RopTrace.SetTestActivitySource(_testActivitySource);
    }

    /// <summary>
    /// Gets the number of activities captured so far.
    /// </summary>
    public int ActivityCount
    {
        get
        {
            lock (_lock)
            {
                return _capturedActivities.Count;
            }
        }
    }

    /// <summary>
    /// Gets a read-only snapshot of all captured activities.
    /// </summary>
    public IReadOnlyList<Activity> CapturedActivities
    {
        get
        {
            lock (_lock)
            {
                return _capturedActivities.ToArray();
            }
        }
    }

    /// <summary>
    /// Waits until the specified number of activities have been captured.
    /// </summary>
    public bool WaitForActivityCount(int expectedCount, TimeSpan? timeout = null)
    {
        var maxWait = timeout ?? TimeSpan.FromSeconds(2);
        return SpinWait.SpinUntil(() => ActivityCount >= expectedCount, maxWait);
    }

    /// <summary>
    /// Waits for an activity with the specified display name to be captured.
    /// </summary>
    public Activity? WaitForActivity(string displayName, TimeSpan? timeout = null)
    {
        var maxWait = timeout ?? TimeSpan.FromSeconds(2);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < maxWait)
        {
            lock (_lock)
            {
                var activity = _capturedActivities.FirstOrDefault(a => a.DisplayName == displayName);
                if (activity != null)
                    return activity;
            }

            Thread.Sleep(10);
        }

        return null;
    }

    /// <summary>
    /// Waits for multiple activities with the specified display name to be captured.
    /// </summary>
    /// <param name="displayName">The activity display name to search for.</param>
    /// <param name="count">The minimum number of activities with this name to wait for.</param>
    /// <param name="timeout">Optional timeout period. Defaults to 2 seconds.</param>
    /// <returns>An enumerable of activities with the specified name, or null if not found within timeout.</returns>
    public IEnumerable<Activity>? WaitForActivities(string displayName, int count, TimeSpan? timeout = null)
    {
        var maxWait = timeout ?? TimeSpan.FromSeconds(2);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < maxWait)
        {
            lock (_lock)
            {
                var activities = _capturedActivities.Where(a => a.DisplayName == displayName);
                if (activities != null && activities.Count() >= count)
                    return activities;
            }

            Thread.Sleep(10);
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Reset RopTrace to use the default ActivitySource
        RopTrace.ResetTestActivitySource();

        // Dispose resources
        _listener.Dispose();
        _testActivitySource.Dispose();

        _disposed = true;
    }
}