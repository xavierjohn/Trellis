namespace Trellis.Testing.Tests.Fakes;

public class FakeSharedResourceLoaderTests
{
    #region Test Aggregate

    private class TestAggregate : Aggregate<string>
    {
        public string Name { get; private set; }

        private TestAggregate(string id, string name) : base(id) => Name = name;

        public static TestAggregate Create(string id, string name) => new(id, name);
    }

    #endregion

    [Fact]
    public void Constructor_Null_Repository_Throws()
    {
        var act = () => new FakeSharedResourceLoader<TestAggregate, string>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task GetByIdAsync_Returns_Success_When_Aggregate_Exists()
    {
        // Arrange
        var repo = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Test");
        await repo.SaveAsync(aggregate, TestContext.Current.CancellationToken);
        var loader = new FakeSharedResourceLoader<TestAggregate, string>(repo);

        // Act
        var result = await loader.GetByIdAsync("1", TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeSuccess()
            .Which.Name.Should().Be("Test");
    }

    [Fact]
    public async Task GetByIdAsync_Returns_NotFound_When_Aggregate_Missing()
    {
        // Arrange
        var repo = new FakeRepository<TestAggregate, string>();
        var loader = new FakeSharedResourceLoader<TestAggregate, string>(repo);

        // Act
        var result = await loader.GetByIdAsync("nonexistent", TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailureOfType<Error.NotFound>();
    }

    [Fact]
    public async Task Loader_Reflects_Repository_State_Changes()
    {
        // Arrange
        var repo = new FakeRepository<TestAggregate, string>();
        var loader = new FakeSharedResourceLoader<TestAggregate, string>(repo);

        // Initially missing
        var before = await loader.GetByIdAsync("1", TestContext.Current.CancellationToken);
        before.Should().BeFailureOfType<Error.NotFound>();

        // Save then load
        await repo.SaveAsync(TestAggregate.Create("1", "Added"), TestContext.Current.CancellationToken);
        var after = await loader.GetByIdAsync("1", TestContext.Current.CancellationToken);
        after.Should().BeSuccess()
            .Which.Name.Should().Be("Added");
    }
}