namespace CookbookSnippets.Tests;

public class Recipe41ContractTests
{
    [Fact]
    public void ActorRateLimiting_Recipe_ProvidesCompiledApplicationOwnedSample() =>
        typeof(Recipe29.IdempotencySample).Assembly
            .GetType("CookbookSnippets.Recipe41.ActorRateLimitingSample").Should().NotBeNull();
}