namespace Trellis.Mediator.Tests;

public class ActorHandlerConsumerTests
{
    [Fact]
    public Task GeneratedDispatch_RuntimePipeline_ExercisesAllSixBasesAndDispatchLifetime()
        => ActorHandlers.Consumer.ConsumerScenarios.RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task GeneratedDispatch_LiteralPipeline_ExercisesAllSixBasesAndClosedContextNormalization()
        => ActorHandlers.GeneratedPipeline.ConsumerScenarios.RunAsync(TestContext.Current.CancellationToken);
}
