namespace Trellis.Testing.AspNetCore.Tests;

using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;

/// <summary>
/// Tests for <see cref="ServiceCollectionExtensions.ReplaceResourceLoader{TMessage, TResource}"/>.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    #region ReplaceResourceLoader

    [Fact]
    public void ReplaceResourceLoader_NoExistingRegistration_RegistersLoader()
    {
        var services = new ServiceCollection();

        services.ReplaceResourceLoader<TestCommand, TestResource>(_ => new FakeResourceLoader());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetRequiredService<IResourceLoader<TestCommand, TestResource>>();
        resolved.Should().BeOfType<FakeResourceLoader>();
    }

    [Fact]
    public void ReplaceResourceLoader_WithExistingRegistration_ReplacesIt()
    {
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<TestCommand, TestResource>>(_ => new FakeResourceLoader());

        services.ReplaceResourceLoader<TestCommand, TestResource>(_ => new AlternateFakeResourceLoader());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetRequiredService<IResourceLoader<TestCommand, TestResource>>();
        resolved.Should().BeOfType<AlternateFakeResourceLoader>();
    }

    [Fact]
    public void ReplaceResourceLoader_WithMultipleExistingRegistrations_ReplacesAll()
    {
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<TestCommand, TestResource>>(_ => new FakeResourceLoader());
        services.AddScoped<IResourceLoader<TestCommand, TestResource>>(_ => new FakeResourceLoader());

        services.ReplaceResourceLoader<TestCommand, TestResource>(_ => new AlternateFakeResourceLoader());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var all = scope.ServiceProvider.GetServices<IResourceLoader<TestCommand, TestResource>>().ToList();
        all.Should().ContainSingle().Which.Should().BeOfType<AlternateFakeResourceLoader>();
    }

    [Fact]
    public void ReplaceResourceLoader_ReturnsServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();

        var returned = services.ReplaceResourceLoader<TestCommand, TestResource>(_ => new FakeResourceLoader());

        returned.Should().BeSameAs(services);
    }

    [Fact]
    public void ReplaceResourceLoader_DoesNotAffectOtherResourceLoaders()
    {
        var services = new ServiceCollection();
        var otherLoader = new OtherFakeResourceLoader();
        services.AddScoped<IResourceLoader<OtherCommand, OtherResource>>(_ => otherLoader);

        services.ReplaceResourceLoader<TestCommand, TestResource>(_ => new FakeResourceLoader());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var other = scope.ServiceProvider.GetRequiredService<IResourceLoader<OtherCommand, OtherResource>>();
        other.Should().BeSameAs(otherLoader);
    }

    #endregion

    #region Scoped Lifetime Tests

    [Fact]
    public void ReplaceResourceLoader_CreatesNewInstancePerScope()
    {
        var services = new ServiceCollection();

        services.ReplaceResourceLoader<TestCommand, TestResource>(_ => new FakeResourceLoader());

        using var provider = services.BuildServiceProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var resolved1 = scope1.ServiceProvider.GetRequiredService<IResourceLoader<TestCommand, TestResource>>();
        var resolved2 = scope2.ServiceProvider.GetRequiredService<IResourceLoader<TestCommand, TestResource>>();

        resolved1.Should().NotBeSameAs(resolved2, "scoped registration creates a new instance per scope");
    }

    [Fact]
    public void ReplaceResourceLoader_SameInstanceWithinScope()
    {
        var services = new ServiceCollection();

        services.ReplaceResourceLoader<TestCommand, TestResource>(_ => new FakeResourceLoader());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var resolved1 = scope.ServiceProvider.GetRequiredService<IResourceLoader<TestCommand, TestResource>>();
        var resolved2 = scope.ServiceProvider.GetRequiredService<IResourceLoader<TestCommand, TestResource>>();

        resolved1.Should().BeSameAs(resolved2, "scoped registration returns the same instance within a scope");
    }

    [Fact]
    public void ReplaceResourceLoader_ReceivesServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton("injected-value");

        IServiceProvider? capturedProvider = null;
        services.ReplaceResourceLoader<TestCommand, TestResource>(sp =>
        {
            capturedProvider = sp;
            return new FakeResourceLoader();
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IResourceLoader<TestCommand, TestResource>>();

        capturedProvider.Should().NotBeNull();
        capturedProvider!.GetRequiredService<string>().Should().Be("injected-value");
    }

    #endregion

    #region ReplaceSingleton

    [Fact]
    public void ReplaceSingleton_RegistersInstance()
    {
        var services = new ServiceCollection();
        var instance = TimeProvider.System;

        services.ReplaceSingleton(instance);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(instance);
    }

    [Fact]
    public void ReplaceSingleton_ReplacesExistingRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        var replacement = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();

        services.ReplaceSingleton<TimeProvider>(replacement);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(replacement);
    }

    [Fact]
    public void ReplaceSingleton_ReturnsServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();

        var returned = services.ReplaceSingleton(TimeProvider.System);

        returned.Should().BeSameAs(services);
    }

    [Fact]
    public void ReplaceSingleton_NullInstance_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.ReplaceSingleton<TimeProvider>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Test Types

    private sealed record TestCommand(string Id);
    private sealed record TestResource(string Name);
    private sealed record OtherCommand(string Id);
    private sealed record OtherResource(string Name);

    private sealed class FakeResourceLoader : IResourceLoader<TestCommand, TestResource>
    {
        public Task<Result<TestResource>> LoadAsync(TestCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new TestResource("test")));
    }

    private sealed class OtherFakeResourceLoader : IResourceLoader<OtherCommand, OtherResource>
    {
        public Task<Result<OtherResource>> LoadAsync(OtherCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new OtherResource("other")));
    }

    private sealed class AlternateFakeResourceLoader : IResourceLoader<TestCommand, TestResource>
    {
        public Task<Result<TestResource>> LoadAsync(TestCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new TestResource("alternate")));
    }

    #endregion
}