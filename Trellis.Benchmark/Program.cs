using Benchmark;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.ShortRun);

if (args.Length == 0)
{
    BenchmarkRunner.Run<BenchmarkROP>(config);
    BenchmarkRunner.Run<BindBenchmarks>(config);
    BenchmarkRunner.Run<MapBenchmarks>(config);
    BenchmarkRunner.Run<TapBenchmarks>(config);
    BenchmarkRunner.Run<EnsureBenchmarks>(config);
    BenchmarkRunner.Run<AsyncBenchmarks>(config);
    BenchmarkRunner.Run<MaybeBenchmarks>(config);
    BenchmarkRunner.Run<ErrorBenchmarks>(config);
    BenchmarkRunner.Run<RecoverBenchmarks>(config);
    BenchmarkRunner.Run<CombineBenchmarks>(config);
    BenchmarkRunner.Run<MatchBenchmarks>(config);
    BenchmarkRunner.Run<MapOnFailureBenchmarks>(config);
    BenchmarkRunner.Run<ValueObjectBenchmarks>(config);
    BenchmarkRunner.Run<SpecificationBenchmarks>(config);
    BenchmarkRunner.Run<MoneyBenchmarks>(config);
    BenchmarkRunner.Run<ActorBenchmarks>(config);
}
else
{
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
}

RunValidationTests();

static void RunValidationTests()
{
    var benchmark = new BenchmarkROP();

    var happyRop = benchmark.RopStyleHappy();
    var happyIf = benchmark.IfStyleHappy();
    if (happyRop != happyIf)
    {
        throw new InvalidDataException("Happy path validation failed: ROP and imperative styles produced different results.");
    }

    var sadRop = benchmark.RopStyleSad();
    var sadIf = benchmark.IfStyleSad();
    if (sadRop != sadIf)
    {
        throw new InvalidDataException("Sad path validation failed: ROP and imperative styles produced different results.");
    }
}