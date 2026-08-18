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
/// Six cells across (ListenerOn=false/true) × (Depth=1/5/10) measure the
/// per-call cost both when no consumer has registered a listener (the production
/// default) and when an OpenTelemetry listener is sampling everything (worst case).
/// <para>
/// An ambient Activity.Current is always installed via the BenchAmbient source, matching
/// the typical ASP.NET case where a request activity is active. BenchAmbient is deliberately
/// <b>not</b> a Trellis-owned source, so it measures the cost of the source-ownership guard
/// in <c>Result&lt;T&gt;.LogActivityStatus()</c> rejecting a foreign ambient span — which is
/// exactly what happens on every Result allocation inside a request. The guard exists so a
/// Result construction cannot stamp status onto the caller's request span.
/// </para>
/// <para>
/// The guarded <c>SetStatus</c> / <c>SetTag</c> writes themselves are exercised by the chain
/// benchmarks when ListenerOn=true: Bind/Map/Tap start a Trellis.Core ROP activity, so Results
/// constructed inside the chain see a Trellis-owned Activity.Current and take the write path.
/// JustOk runs outside any ROP activity and therefore measures constructor + guard rejection only.
/// </para>
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
        // during all benchmark runs, matching the typical ASP.NET case where a request activity
        // is active. BenchAmbient is not a Trellis-owned source, so Result<T>.LogActivityStatus()
        // takes its guard-rejection path — the cost paid on every Result allocation in a request.
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

        // Ambient activity models a live request span; see the class remarks for what it exercises.
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

    /// <summary>Just constructs Results — no extensions. Measures constructor cost plus the ambient-source guard rejection.</summary>
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