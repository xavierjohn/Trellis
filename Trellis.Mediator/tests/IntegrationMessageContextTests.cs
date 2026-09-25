namespace Trellis.Mediator.Tests;

public sealed class IntegrationMessageContextTests
{
    [Fact]
    public void BeginProcessing_InboundCorrelationTakesPrecedenceAndRestoresApplicationContext()
    {
        var messageId = Guid.CreateVersion7();
        using (IntegrationMessageContext.BeginCorrelation("application", "catalog"))
        {
            using (IntegrationMessageContext.BeginProcessing(new IntegrationEnvelope(
                messageId, new SampleIntegrationEvent(DateTimeOffset.UnixEpoch))
            {
                CorrelationId = "inbound-workflow",
                MessageSource = "upstream",
                TraceParent = "00-ec304e482c83425f9792e53730758760-a0d5aaefcdebbf13-01",
            }))
            {
                IntegrationMessageContext.CurrentMessageId.Should().Be(messageId);
                IntegrationMessageContext.CorrelationId.Should().Be("inbound-workflow");
                IntegrationMessageContext.MessageSource.Should().Be("catalog");
                IntegrationMessageContext.TraceParent.Should().NotBeNull();

                using (IntegrationMessageContext.BeginCorrelation("nested"))
                    IntegrationMessageContext.CorrelationId.Should().Be("inbound-workflow");
            }

            IntegrationMessageContext.CurrentMessageId.Should().BeNull();
            IntegrationMessageContext.CorrelationId.Should().Be("application");
        }

        IntegrationMessageContext.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void BeginProcessing_EmptyInboundCorrelationUsesExplicitApplicationValue()
    {
        using (IntegrationMessageContext.BeginCorrelation("explicit"))
        using (IntegrationMessageContext.BeginProcessing(new IntegrationEnvelope(
            Guid.CreateVersion7(), new SampleIntegrationEvent(DateTimeOffset.UnixEpoch))
        {
            CorrelationId = "   ",
            TraceParent = "invalid",
            TraceState = "vendor=untrusted",
        }))
        {
            IntegrationMessageContext.CorrelationId.Should().Be("explicit");
            IntegrationMessageContext.TraceParent.Should().BeNull();
            IntegrationMessageContext.TraceState.Should().BeNull();
        }
    }

    [Fact]
    public void BeginProcessing_OpaqueTraceStateDoesNotBlockValidTraceParent()
    {
        const string traceParent = "00-ec304e482c83425f9792e53730758760-a0d5aaefcdebbf13-01";
        using (IntegrationMessageContext.BeginProcessing(new IntegrationEnvelope(
            Guid.CreateVersion7(), new SampleIntegrationEvent(DateTimeOffset.UnixEpoch))
        {
            TraceParent = traceParent,
            TraceState = "invalid=;",
        }))
        {
            IntegrationMessageContext.TraceParent.Should().Be(traceParent);
            IntegrationMessageContext.TraceState.Should().Be("invalid=;");
        }
    }

    [Fact]
    public void BeginProcessing_NestedMessageUsesItsOwnLineageAndExplicitOuterCorrelation()
    {
        var nestedMessageId = Guid.CreateVersion7();
        using (IntegrationMessageContext.BeginProcessing(new IntegrationEnvelope(
            Guid.CreateVersion7(), new SampleIntegrationEvent(DateTimeOffset.UnixEpoch))
        {
            CorrelationId = "first-workflow",
            TraceParent = "00-ec304e482c83425f9792e53730758760-a0d5aaefcdebbf13-01",
        }))
        using (IntegrationMessageContext.BeginCorrelation("application-workflow"))
        using (IntegrationMessageContext.BeginProcessing(new IntegrationEnvelope(
            nestedMessageId, new SampleIntegrationEvent(DateTimeOffset.UnixEpoch))))
        {
            IntegrationMessageContext.CurrentMessageId.Should().Be(nestedMessageId);
            IntegrationMessageContext.CorrelationId.Should().Be("application-workflow");
            IntegrationMessageContext.TraceParent.Should().BeNull();
            IntegrationMessageContext.TraceState.Should().BeNull();
        }
    }

    [Fact]
    public async Task BeginProcessing_DisposedScopeDoesNotLeakToOrphanChildTask()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(Guid?, string?)> child;

        using (IntegrationMessageContext.BeginProcessing(new IntegrationEnvelope(
            Guid.CreateVersion7(), new SampleIntegrationEvent(DateTimeOffset.UnixEpoch))
        {
            CorrelationId = "private-workflow",
        }))
        {
            child = Task.Run(async () =>
            {
                await gate.Task;
                return (IntegrationMessageContext.CurrentMessageId, IntegrationMessageContext.CorrelationId);
            });
        }

        gate.SetResult();
        var observed = await child;
        observed.Item1.Should().BeNull();
        observed.Item2.Should().BeNull();
    }

    [Fact]
    public async Task BeginCorrelation_ChildScopeDoesNotRetainDisposedProcessingLineage()
    {
        const string traceParent = "00-ec304e482c83425f9792e53730758760-a0d5aaefcdebbf13-01";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(Guid? MessageId, string? CorrelationId, string? MessageSource,
            string? TraceParent, string? TraceState)> child;

        using (IntegrationMessageContext.BeginProcessing(new IntegrationEnvelope(
            Guid.CreateVersion7(), new SampleIntegrationEvent(DateTimeOffset.UnixEpoch))
        {
            TraceParent = traceParent,
            TraceState = "vendor=inbound",
        }))
        {
            child = Task.Run(async () =>
            {
                using (IntegrationMessageContext.BeginCorrelation("nested", "child-source"))
                {
                    entered.SetResult();
                    await resume.Task;
                    return (IntegrationMessageContext.CurrentMessageId,
                        IntegrationMessageContext.CorrelationId, IntegrationMessageContext.MessageSource,
                        IntegrationMessageContext.TraceParent, IntegrationMessageContext.TraceState);
                }
            });
            await entered.Task;
        }

        resume.SetResult();
        var observed = await child;
        observed.MessageId.Should().BeNull();
        observed.CorrelationId.Should().Be("nested");
        observed.MessageSource.Should().Be("child-source");
        observed.TraceParent.Should().BeNull();
        observed.TraceState.Should().BeNull();
    }

    [Fact]
    public async Task BeginCorrelation_OrphanChildNestsFreshScopesWithoutLosingDisposalOrder()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextMessageId = Guid.CreateVersion7();
        Task child;

        using (IntegrationMessageContext.BeginProcessing(new IntegrationEnvelope(
            Guid.CreateVersion7(), new SampleIntegrationEvent(DateTimeOffset.UnixEpoch))
        {
            CorrelationId = "previous-workflow",
        }))
        {
            child = Task.Run(async () =>
            {
                using (IntegrationMessageContext.BeginCorrelation("orphan"))
                {
                    entered.SetResult();
                    await resume.Task;
                    IntegrationMessageContext.CurrentMessageId.Should().BeNull();

                    using (IntegrationMessageContext.BeginCorrelation("fresh-workflow"))
                    {
                        IntegrationMessageContext.CurrentMessageId.Should().BeNull();
                        IntegrationMessageContext.CorrelationId.Should().Be("fresh-workflow");

                        using (IntegrationMessageContext.BeginProcessing(new IntegrationEnvelope(
                            nextMessageId, new SampleIntegrationEvent(DateTimeOffset.UnixEpoch))))
                        {
                            IntegrationMessageContext.CurrentMessageId.Should().Be(nextMessageId);
                            IntegrationMessageContext.CorrelationId.Should().Be("fresh-workflow");
                        }

                        IntegrationMessageContext.CorrelationId.Should().Be("fresh-workflow");
                    }

                    IntegrationMessageContext.CurrentMessageId.Should().BeNull();
                }

                IntegrationMessageContext.CurrentMessageId.Should().BeNull();
            }, TestContext.Current.CancellationToken);
            await entered.Task;
        }

        resume.SetResult();
        await child;
    }

    [Theory]
    [InlineData("child-workflow")]
    [InlineData(null)]
    public async Task BeginProcessing_ChildRetainsOwnLineageAfterOuterCorrelationEnds(string? inboundCorrelationId)
    {
        const string traceParent = "00-ec304e482c83425f9792e53730758760-a0d5aaefcdebbf13-01";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var childMessageId = Guid.CreateVersion7();
        Task child;

        using (IntegrationMessageContext.BeginCorrelation("parent-workflow", "parent-source"))
        {
            child = Task.Run(async () =>
            {
                using (IntegrationMessageContext.BeginProcessing(new IntegrationEnvelope(
                    childMessageId, new SampleIntegrationEvent(DateTimeOffset.UnixEpoch))
                {
                    CorrelationId = inboundCorrelationId,
                    TraceParent = traceParent,
                    TraceState = "vendor=child",
                }))
                {
                    entered.SetResult();
                    await resume.Task;

                    IntegrationMessageContext.CurrentMessageId.Should().Be(childMessageId);
                    IntegrationMessageContext.CorrelationId.Should().Be(inboundCorrelationId);
                    IntegrationMessageContext.MessageSource.Should().BeNull();
                    IntegrationMessageContext.TraceParent.Should().Be(traceParent);
                    IntegrationMessageContext.TraceState.Should().Be("vendor=child");

                    using (IntegrationMessageContext.BeginCorrelation("child-explicit", "child-source"))
                    {
                        IntegrationMessageContext.CurrentMessageId.Should().Be(childMessageId);
                        IntegrationMessageContext.CorrelationId.Should().Be(inboundCorrelationId ?? "child-explicit");
                        IntegrationMessageContext.MessageSource.Should().Be("child-source");
                        IntegrationMessageContext.TraceParent.Should().Be(traceParent);
                    }
                }
            }, TestContext.Current.CancellationToken);
            await entered.Task;
        }

        resume.SetResult();
        await child;
    }

    [Fact]
    public void BeginCorrelation_BlankValue_Throws()
    {
        Action act = () => IntegrationMessageContext.BeginCorrelation(" ");

        act.Should().Throw<ArgumentException>();
    }

    private sealed record SampleIntegrationEvent(DateTimeOffset OccurredAt) : IIntegrationEvent;
}
