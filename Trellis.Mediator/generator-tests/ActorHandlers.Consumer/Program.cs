#if GENERATED_PIPELINE
using ActorHandlers.GeneratedPipeline;
#else
using ActorHandlers.Consumer;
#endif

await ConsumerScenarios.RunAsync();
Console.WriteLine("Actor handler generated-dispatch contracts passed.");
