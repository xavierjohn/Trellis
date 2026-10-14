namespace Trellis.Testing.Tests;

using Trellis.Authorization;

public class ActorIdentityAssertionsContractTests
{
    [Fact]
    public void ActorIdentityAssertions_PublicContract_ProvidesOneLineAgreementAssertion()
    {
        var type = typeof(TestActorProvider).Assembly.GetType("Trellis.Testing.ActorIdentityAssertionsExtensions");
        type.Should().NotBeNull();
        var method = type!.GetMethod("ShouldHaveConsistentActorIdentityAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be<Task>();
        method.GetParameters().Select(p => p.ParameterType)
            .Should().Equal(typeof(IActorProvider), typeof(IActorIdentityProvider), typeof(CancellationToken));
    }

    [Fact]
    public void TestActorProvider_IdentityContract_OptsInWithoutRequiringFullResolution() =>
        typeof(TestActorProvider).Should().Implement<IActorIdentityProvider>();
}