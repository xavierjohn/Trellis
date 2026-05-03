namespace Benchmark;

using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Trellis;

/// <summary>
/// Measures the overhead of Trellis.Core's per-operation Activity tracing
/// (Result&lt;T&gt; constructor's Activity.Current?.SetStatus(...) plus each
/// extension's using var activity = ActivitySource.StartActivity(...)).
/// </summary>
/// <remarks>
/// Four cells across (ListenerOn=false/true) × (Depth=1/5/10) measure the
/// per-call cost both when no consumer has registered a listener (the production
/// default) and when an OpenTelemetry listener is sampling everything (worst case).
/// An ambient Activity.Current is always installed via the BenchAmbient source so
/// the constructor side-effect is exercised on every Result allocation regardless of
/// whether the Trellis.Core source itself has a listener.
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet manages lifecycle via [GlobalSetup]/[GlobalCleanup]; disposing in [GlobalCleanup] is the canonical pattern.")]
public class TracingOverheadBenchmarks
{
    private static readonly ActivitySource AmbientSource = new("BenchAmbient");

    static TracingOverheadBenchmarks()
    {
        // Always-on listener for the BenchAmbient source ensures Activity.Current is non-null
        // during all benchmark runs so the Result<T> constructor's SetStatus side effect
        // actually does work (matching the typical ASP.NET case where a request activity is active).
        var ambientListener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "BenchAmbient",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(ambientListener);
    }

    [Params(false, true)]
    public bool ListenerOn { get; set; }

    [Params(1, 5, 10)]
    public int Depth { get; set; }

    private ActivityListener? _trellisListener;
    private Activity? _ambient;
    private Result<int> _success;
    private Result<int> _failure;

    [GlobalSetup]
    public void Setup()
    {
        _success = Result.Ok(42);
        _failure = Result.Fail<int>(new Error.Unexpected("benchmark"));

        // Ambient activity ensures the Result<T> constructor's SetStatus path is exercised.
        _ambient = AmbientSource.StartActivity("bench-ambient");

        if (ListenerOn)
        {
            _trellisListener = new ActivityListener
            {
                ShouldListenTo = src => src.Name == "Trellis.Core",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = _ => { },
                ActivityStopped = _ => { },
            };
            ActivitySource.AddActivityListener(_trellisListener);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _trellisListener?.Dispose();
        _ambient?.Dispose();
    }

    /// <summary>Just constructs Results — no extensions. Measures pure constructor cost (incl. Activity side-effect).</summary>
    [Benchmark]
    public Result<int> JustOk()
    {
        Result<int> r = default;
        for (var i = 0; i < Depth; i++) r = Result.Ok(42);
        return r;
    }

    /// <summary>Bind chain, all success — measures combined constructor + StartActivity overhead.</summary>
    [Benchmark]
    public Result<int> BindChain_AllSuccess()
    {
        var r = _success;
        for (var i = 0; i < Depth; i++) r = r.Bind(x => Result.Ok(x + 1));
        return r;
    }

    /// <summary>Bind chain, fails immediately — measures short-circuit cost (extensions still call StartActivity).</summary>
    [Benchmark]
    public Result<int> BindChain_FailAtFirst()
    {
        var r = _failure;
        for (var i = 0; i < Depth; i++) r = r.Bind(x => Result.Ok(x + 1));
        return r;
    }

    /// <summary>Map chain, all success — same shape as Bind but no nested Result.</summary>
    [Benchmark]
    public Result<int> MapChain_AllSuccess()
    {
        var r = _success;
        for (var i = 0; i < Depth; i++) r = r.Map(x => x + 1);
        return r;
    }

    /// <summary>Tap chain — pure side-effect; measures constructor + StartActivity overhead with no value transformation.</summary>
    [Benchmark]
    public Result<int> TapChain_AllSuccess()
    {
        var r = _success;
        var sink = 0;
        for (var i = 0; i < Depth; i++) r = r.Tap(x => sink += x);
        return r;
    }
}
