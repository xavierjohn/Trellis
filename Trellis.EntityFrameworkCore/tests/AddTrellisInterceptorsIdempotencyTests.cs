namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

public class AddTrellisInterceptorsIdempotencyTests
{
    private static readonly Type[] _trellisInterceptorTypes =
    [
        typeof(MaybeQueryInterceptor),
        typeof(ScalarValueQueryInterceptor),
        typeof(AggregateETagInterceptor),
        typeof(EntityTimestampInterceptor)
    ];

    [Fact]
    public void AddTrellisInterceptors_calledOnce_registersEachInterceptorExactlyOnce()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .AddTrellisInterceptors()
            .Options;

        var interceptors = GetInterceptors(options);

        AssertTrellisInterceptorsRegisteredExactlyOnce(interceptors);
    }

    [Fact]
    public void AddTrellisInterceptors_calledTwice_remainsIdempotent()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .AddTrellisInterceptors()
            .AddTrellisInterceptors()
            .Options;

        var interceptors = GetInterceptors(options);

        AssertTrellisInterceptorsRegisteredExactlyOnce(interceptors);
    }

    [Fact]
    public void AddTrellisInterceptors_withConsumerInterceptor_preservesConsumerInterceptor()
    {
        var consumerInterceptor = new ConsumerInterceptor();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .AddInterceptors(consumerInterceptor)
            .AddTrellisInterceptors()
            .Options;

        var interceptors = GetInterceptors(options);

        interceptors.Should().ContainSingle(interceptor => ReferenceEquals(interceptor, consumerInterceptor));
        AssertTrellisInterceptorsRegisteredExactlyOnce(interceptors);
    }

    [Fact]
    public void AddTrellisInterceptors_calledTwiceWithConsumerInterceptorBetween_preservesAll()
    {
        var consumerInterceptor = new ConsumerInterceptor();
        var builder = new DbContextOptionsBuilder<TestDbContext>()
            .AddTrellisInterceptors();

        builder.AddInterceptors(consumerInterceptor);
        builder.AddTrellisInterceptors();

        var interceptors = GetInterceptors(builder.Options);

        interceptors.Should().ContainSingle(interceptor => ReferenceEquals(interceptor, consumerInterceptor));
        AssertTrellisInterceptorsRegisteredExactlyOnce(interceptors);
    }

    [Fact]
    public void AddTrellisInterceptors_calledTwiceWithSameTimeProvider_remainsIdempotent()
    {
        var timeProvider = TimeProvider.System;
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .AddTrellisInterceptors(timeProvider)
            .AddTrellisInterceptors(timeProvider)
            .Options;

        var interceptors = GetInterceptors(options);

        AssertTrellisInterceptorsRegisteredExactlyOnce(interceptors);
    }

    [Fact]
    public void AddTrellisInterceptors_conflictingTimeProviders_throws()
    {
        var firstClock = new FakeTimeProvider();
        var secondClock = new FakeTimeProvider();
        var builder = new DbContextOptionsBuilder<TestDbContext>()
            .AddTrellisInterceptors(firstClock);

        var act = () => builder.AddTrellisInterceptors(secondClock);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already called*TimeProvider*Consolidate*");
    }

    [Fact]
    public void AddTrellisInterceptors_parameterlessAfterCustomTimeProvider_throws()
    {
        // The parameterless overload implies TimeProvider.System; following a custom-clock
        // registration this is still a conflict (the consumer's custom clock would be
        // silently shadowed). Better to fail fast.
        var customClock = new FakeTimeProvider();
        var builder = new DbContextOptionsBuilder<TestDbContext>()
            .AddTrellisInterceptors(customClock);

        var act = () => builder.AddTrellisInterceptors();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already called*TimeProvider*");
    }

    [Fact]
    public void AddTrellisInterceptors_customTimeProviderAfterParameterless_throws()
    {
        // The reverse of the previous test — library calls parameterless first, then app
        // tries to supply a custom clock. Without the fail-fast the app's clock is silently
        // dropped.
        var customClock = new FakeTimeProvider();
        var builder = new DbContextOptionsBuilder<TestDbContext>()
            .AddTrellisInterceptors();

        var act = () => builder.AddTrellisInterceptors(customClock);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already called*TimeProvider*");
    }

    private static List<IInterceptor> GetInterceptors(DbContextOptions options)
    {
        var coreOptions = options.FindExtension<CoreOptionsExtension>();
        coreOptions.Should().NotBeNull();
        return coreOptions!.Interceptors?.ToList() ?? [];
    }

    private static void AssertTrellisInterceptorsRegisteredExactlyOnce(IReadOnlyCollection<IInterceptor> interceptors)
    {
        foreach (var interceptorType in _trellisInterceptorTypes)
        {
            interceptors.Count(interceptor => interceptor.GetType() == interceptorType)
                .Should().Be(1, $"{interceptorType.Name} should be registered exactly once");
        }
    }

    private sealed class ConsumerInterceptor : IInterceptor;

    private sealed class FakeTimeProvider : TimeProvider;
}