namespace Trellis.ServiceDefaults.Tests;

using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;

public sealed class OutboxReadinessBuilderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UseOutboxReadiness_BeforeOrAfterUnitOfWork_PreservesCanonicalPipeline(bool readinessFirst)
    {
        var services = new ServiceCollection();
        var profile = OutboxReadinessOptions.CreateStagedOperationalProfile();
        services.AddTrellis(builder =>
        {
            builder.UseDomainEvents().UseOutbox<ReadinessDbContext>();
            if (readinessFirst)
                builder.UseOutboxReadiness<ReadinessDbContext>(profile);
            builder.UseEntityFrameworkUnitOfWork<ReadinessDbContext>();
            if (!readinessFirst)
                builder.UseOutboxReadiness<ReadinessDbContext>(profile);
        });
        using var provider = services.BuildServiceProvider();

        var pipeline = services.Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType).ToList();
        pipeline.Should().EndWith(typeof(TransactionalCommandBehavior<,>));
        var baseline = new ServiceCollection();
        baseline.AddTrellis(builder => builder.UseDomainEvents().UseEntityFrameworkUnitOfWork<ReadinessDbContext>());
        pipeline.Should().Equal(baseline.Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType));
        provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Should().ContainSingle(r => r.Name == "trellis-outbox");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UseOutboxReadiness_DirectRegistrationBeforeOrAfterBuilder_RegistersOnce(bool directFirst)
    {
        var services = new ServiceCollection();
        var profile = OutboxReadinessOptions.CreateStagedOperationalProfile();
        if (directFirst)
            services.AddTrellisOutboxReadiness<ReadinessDbContext>(profile);
        services.AddTrellis(b => b.UseOutbox<ReadinessDbContext>().UseOutboxReadiness<ReadinessDbContext>(profile));
        if (!directFirst)
            services.AddTrellisOutboxReadiness<ReadinessDbContext>(profile);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Should().ContainSingle(r => r.Name == "trellis-outbox");
    }

    [Fact]
    public void UseOutboxReadiness_RepeatedSelection_Throws()
    {
        var services = new ServiceCollection();
        var profile = OutboxReadinessOptions.CreateStagedOperationalProfile();

        var act = () => services.AddTrellis(b => b
            .UseOutboxReadiness<ReadinessDbContext>(profile)
            .UseOutboxReadiness<ReadinessDbContext>(profile));

        act.Should().Throw<InvalidOperationException>().WithMessage("*readiness*");
    }

    private sealed class ReadinessDbContext(DbContextOptions<ReadinessDbContext> options) : DbContext(options);
}