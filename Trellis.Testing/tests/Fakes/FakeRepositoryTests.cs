namespace Trellis.Testing.Tests.Fakes;

public class FakeRepositoryTests
{
    #region Test Aggregate

    private record TestEvent(string AggregateId, DateTimeOffset OccurredAt) : IDomainEvent
    {
        public TestEvent(string aggregateId) : this(aggregateId, DateTimeOffset.UtcNow) { }
    }

    private class TestAggregate : Aggregate<string>
    {
        public string Name { get; private set; }
        public string Email { get; private set; }

        private TestAggregate(string id, string name, string email = "default@test.com") : base(id)
        {
            Name = name;
            Email = email;
        }

        public static TestAggregate Create(string id, string name, string email = "default@test.com")
        {
            var aggregate = new TestAggregate(id, name, email);
            aggregate.DomainEvents.Add(new TestEvent(id));
            return aggregate;
        }

        public void UpdateName(string newName)
        {
            Name = newName;
            DomainEvents.Add(new TestEvent(Id));
        }
    }

    #endregion

    #region SaveAsync Tests

    [Fact]
    public async Task SaveAsync_Should_Store_Aggregate()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Test");

        // Act
        var result = await repository.SaveAsync(aggregate, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeSuccess();
        repository.Count.Should().Be(1);
        repository.Exists("1").Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_Should_Capture_Domain_Events()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Test");

        // Act
        await repository.SaveAsync(aggregate, TestContext.Current.CancellationToken);

        // Assert
        repository.PublishedEvents.Should().HaveCount(1);
        repository.PublishedEvents[0].Should().BeOfType<TestEvent>();
    }

    [Fact]
    public async Task SaveAsync_Should_Update_Existing_Aggregate()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Original");
        await repository.SaveAsync(aggregate, TestContext.Current.CancellationToken);
        var eventsAfterFirstSave = repository.PublishedEvents.Count;

        aggregate.UpdateName("Updated");

        // Act
        await repository.SaveAsync(aggregate, TestContext.Current.CancellationToken);

        // Assert
        repository.Count.Should().Be(1);
        repository.Get("1")!.Name.Should().Be("Updated");
        repository.PublishedEvents.Count.Should().BeGreaterThan(eventsAfterFirstSave);
    }

    #endregion

    #region GetByIdAsync Tests

    [Fact]
    public async Task GetByIdAsync_Should_Return_Aggregate_When_Exists()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Test");
        await repository.SaveAsync(aggregate, TestContext.Current.CancellationToken);

        // Act
        var result = await repository.GetByIdAsync("1", TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Name.Should().Be("Test");
    }

    [Fact]
    public async Task GetByIdAsync_Should_Return_NotFound_When_Not_Exists()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();

        // Act
        var result = await repository.GetByIdAsync("nonexistent", TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailureOfType<Error.NotFound>();
    }

    #endregion

    #region FindByIdAsync Tests

    [Fact]
    public async Task FindByIdAsync_Should_Return_Maybe_With_Value_When_Exists()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Test");
        await repository.SaveAsync(aggregate, TestContext.Current.CancellationToken);

        // Act
        var maybe = await repository.FindByIdAsync("1", TestContext.Current.CancellationToken);

        // Assert
        maybe.HasValue.Should().BeTrue();
        maybe.Value.Name.Should().Be("Test");
    }

    [Fact]
    public async Task FindByIdAsync_Should_Return_Maybe_None_When_Not_Exists()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();

        // Act
        var maybe = await repository.FindByIdAsync("nonexistent", TestContext.Current.CancellationToken);

        // Assert
        maybe.HasNoValue.Should().BeTrue();
    }

    #endregion

    #region Add (staging) Tests — mirrors RepositoryBase.Add for handler/UoW ergonomics

    [Fact]
    public void Add_Should_Store_Aggregate()
    {
        var repository = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Test");

        repository.Add(aggregate);

        repository.Count.Should().Be(1);
        repository.Exists("1").Should().BeTrue();
    }

    [Fact]
    public void Add_Should_Capture_Domain_Events()
    {
        var repository = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Test");

        repository.Add(aggregate);

        repository.PublishedEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TestEvent>()
            .Which.AggregateId.Should().Be("1");
    }

    [Fact]
    public void Add_Should_AcceptChanges_OnAggregate()
    {
        var repository = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Test");

        repository.Add(aggregate);

        // After Add, the aggregate's uncommitted events should be cleared (committed to fake's event log).
        aggregate.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public void Add_Should_Update_Existing_Aggregate()
    {
        // Add behaves like SaveAsync for the existing-key case: latest write wins.
        var repository = new FakeRepository<TestAggregate, string>();
        var first = TestAggregate.Create("1", "First");
        repository.Add(first);

        var updated = TestAggregate.Create("1", "Updated");
        repository.Add(updated);

        repository.Count.Should().Be(1);
        repository.Get("1")!.Name.Should().Be("Updated");
    }

    [Fact]
    public void Add_Should_ThrowInvalidOperationException_When_UniqueConstraint_Violated()
    {
        // Sonnet 4.6 lab feedback: setup-time violations should fail loud at the Add call site,
        // not be deferred to a later Result. Tests use Add for setup ("put this in the store");
        // failure means the test setup itself is wrong.
        var repository = new FakeRepository<TestAggregate, string>()
            .WithUniqueConstraint(a => a.Email);

        repository.Add(TestAggregate.Create("1", "Alice", "alice@test.com"));

        var act = () => repository.Add(TestAggregate.Create("2", "Bob", "alice@test.com"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unique constraint*");
    }

    [Fact]
    public void Add_Should_AllowSameId_Update_With_UniqueConstraint()
    {
        var repository = new FakeRepository<TestAggregate, string>()
            .WithUniqueConstraint(a => a.Email);
        var aggregate = TestAggregate.Create("1", "Alice", "alice@test.com");
        repository.Add(aggregate);

        // Same ID, same email — update path, no conflict.
        var act = () => repository.Add(aggregate);

        act.Should().NotThrow();
        repository.Count.Should().Be(1);
    }

    [Fact]
    public void Add_Should_Throw_ArgumentNullException_For_Null()
    {
        var repository = new FakeRepository<TestAggregate, string>();

        var act = () => repository.Add(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Remove (staging) Tests — mirrors RepositoryBase.Remove

    [Fact]
    public void Remove_Should_Delete_Aggregate_When_Exists()
    {
        var repository = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Test");
        repository.Add(aggregate);

        repository.Remove(aggregate);

        repository.Count.Should().Be(0);
        repository.Exists("1").Should().BeFalse();
    }

    [Fact]
    public void Remove_Should_Be_NoOp_When_Not_Tracked()
    {
        // Mirrors RepositoryBase.Remove(DbSet.Remove) which marks for delete without verifying existence.
        // The fake should not throw when the aggregate isn't in the store — handlers shouldn't have
        // to defensively Find before Remove for the void Remove(T) path.
        var repository = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Test");

        var act = () => repository.Remove(aggregate);

        act.Should().NotThrow();
        repository.Count.Should().Be(0);
    }

    [Fact]
    public void Remove_Should_Throw_ArgumentNullException_For_Null()
    {
        var repository = new FakeRepository<TestAggregate, string>();

        var act = () => repository.Remove(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region RemoveByIdAsync Tests — mirrors RepositoryBase.RemoveByIdAsync naming

    [Fact]
    public async Task RemoveByIdAsync_Should_Remove_Aggregate_When_Exists()
    {
        var repository = new FakeRepository<TestAggregate, string>();
        repository.Add(TestAggregate.Create("1", "Test"));

        var result = await repository.RemoveByIdAsync("1", TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        repository.Count.Should().Be(0);
    }

    [Fact]
    public async Task RemoveByIdAsync_Should_Return_NotFound_When_Not_Exists()
    {
        var repository = new FakeRepository<TestAggregate, string>();

        var result = await repository.RemoveByIdAsync("nonexistent", TestContext.Current.CancellationToken);

        result.Should().BeFailureOfType<Error.NotFound>();
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_Should_Remove_Aggregate_When_Exists()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Test");
        await repository.SaveAsync(aggregate, TestContext.Current.CancellationToken);

        // Act
        var result = await repository.DeleteAsync("1", TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeSuccess();
        repository.Count.Should().Be(0);
        repository.Exists("1").Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_Should_Return_NotFound_When_Not_Exists()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();

        // Act
        var result = await repository.DeleteAsync("nonexistent", TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailureOfType<Error.NotFound>();
    }

    #endregion

    #region Clear Tests

    [Fact]
    public async Task Clear_Should_Remove_All_Aggregates_And_Events()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();
        await repository.SaveAsync(TestAggregate.Create("1", "Test1"), TestContext.Current.CancellationToken);
        await repository.SaveAsync(TestAggregate.Create("2", "Test2"), TestContext.Current.CancellationToken);

        // Act
        repository.Clear();

        // Assert
        repository.Count.Should().Be(0);
        repository.PublishedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishedEvents_ReturnsLiveView()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();
        await repository.SaveAsync(TestAggregate.Create("1", "Test1"), TestContext.Current.CancellationToken);
        await repository.SaveAsync(TestAggregate.Create("2", "Test2"), TestContext.Current.CancellationToken);

        // Act
        var publishedEvents = repository.PublishedEvents;

        // Assert — live wrapper reflects current state
        publishedEvents.Should().HaveCount(2);
        repository.Clear();
        publishedEvents.Should().BeEmpty();
        repository.PublishedEvents.Should().BeEmpty();
    }

    #endregion

    #region Get and GetAll Tests

    [Fact]
    public async Task Get_Should_Return_Aggregate_When_Exists()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Test");
        await repository.SaveAsync(aggregate, TestContext.Current.CancellationToken);

        // Act
        var result = repository.Get("1");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
    }

    [Fact]
    public void Get_Should_Return_Null_When_Not_Exists()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();

        // Act
        var result = repository.Get("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAll_Should_Return_All_Aggregates()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();
        await repository.SaveAsync(TestAggregate.Create("1", "Test1"), TestContext.Current.CancellationToken);
        await repository.SaveAsync(TestAggregate.Create("2", "Test2"), TestContext.Current.CancellationToken);
        await repository.SaveAsync(TestAggregate.Create("3", "Test3"), TestContext.Current.CancellationToken);

        // Act
        var all = repository.GetAll().ToList();

        // Assert
        all.Should().HaveCount(3);
        all.Select(a => a.Name).Should().Contain(["Test1", "Test2", "Test3"]);
    }

    [Fact]
    public async Task GetAll_ReturnsSnapshot_NotLiveView()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();
        await repository.SaveAsync(TestAggregate.Create("1", "Test1"), TestContext.Current.CancellationToken);
        await repository.SaveAsync(TestAggregate.Create("2", "Test2"), TestContext.Current.CancellationToken);

        // Act
        var all = repository.GetAll().ToList();
        repository.Clear();

        // Assert
        all.Should().HaveCount(2);
        repository.GetAll().Should().BeEmpty();
    }

    #endregion

    #region Exists Tests

    [Fact]
    public async Task Exists_Should_Return_True_When_Aggregate_Exists()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();
        await repository.SaveAsync(TestAggregate.Create("1", "Test"), TestContext.Current.CancellationToken);

        // Act & Assert
        repository.Exists("1").Should().BeTrue();
    }

    [Fact]
    public void Exists_Should_Return_False_When_Aggregate_Not_Exists()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>();

        // Act & Assert
        repository.Exists("nonexistent").Should().BeFalse();
    }

    #endregion

    #region Unique Constraint Tests

    [Fact]
    public async Task SaveAsync_WithUniqueConstraint_ReturnsConflict_OnDuplicate()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>()
            .WithUniqueConstraint(a => a.Email);

        await repository.SaveAsync(TestAggregate.Create("1", "Alice", "alice@test.com"), TestContext.Current.CancellationToken);

        // Act — different ID, same email
        var result = await repository.SaveAsync(
            TestAggregate.Create("2", "Bob", "alice@test.com"), TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailureOfType<Error.Conflict>();
        repository.Count.Should().Be(1);
    }

    [Fact]
    public async Task SaveAsync_WithUniqueConstraint_AllowsUpdate_SameEntity()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>()
            .WithUniqueConstraint(a => a.Email);

        var aggregate = TestAggregate.Create("1", "Alice", "alice@test.com");
        await repository.SaveAsync(aggregate, TestContext.Current.CancellationToken);

        // Act — update same entity (same ID, same email)
        aggregate.UpdateName("Alice Updated");
        var result = await repository.SaveAsync(aggregate, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeSuccess();
        repository.Get("1")!.Name.Should().Be("Alice Updated");
    }

    [Fact]
    public async Task SaveAsync_WithMultipleUniqueConstraints_ChecksAll()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>()
            .WithUniqueConstraint(a => a.Email)
            .WithUniqueConstraint(a => a.Name);

        await repository.SaveAsync(TestAggregate.Create("1", "Alice", "alice@test.com"), TestContext.Current.CancellationToken);

        // Act — different email but same name
        var result = await repository.SaveAsync(
            TestAggregate.Create("2", "Alice", "bob@test.com"), TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailureOfType<Error.Conflict>();
    }

    [Fact]
    public async Task SaveAsync_WithUniqueConstraint_AllowsDifferentValues()
    {
        // Arrange
        var repository = new FakeRepository<TestAggregate, string>()
            .WithUniqueConstraint(a => a.Email);

        await repository.SaveAsync(TestAggregate.Create("1", "Alice", "alice@test.com"), TestContext.Current.CancellationToken);

        // Act — different email
        var result = await repository.SaveAsync(
            TestAggregate.Create("2", "Bob", "bob@test.com"), TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeSuccess();
        repository.Count.Should().Be(2);
    }

    #endregion

    #region FindAsync (predicate) Tests

    [Fact]
    public async Task FindAsync_Returns_Maybe_With_Value_When_Match_Exists()
    {
        var repository = new FakeRepository<TestAggregate, string>();
        await repository.SaveAsync(TestAggregate.Create("1", "Alice", "alice@test.com"), TestContext.Current.CancellationToken);
        await repository.SaveAsync(TestAggregate.Create("2", "Bob", "bob@test.com"), TestContext.Current.CancellationToken);

        var maybe = await repository.FindAsync(a => a.Email == "alice@test.com");

        maybe.Should().HaveValue();
        maybe.Value.Name.Should().Be("Alice");
    }

    [Fact]
    public async Task FindAsync_Returns_None_When_No_Match()
    {
        var repository = new FakeRepository<TestAggregate, string>();
        await repository.SaveAsync(TestAggregate.Create("1", "Alice", "alice@test.com"), TestContext.Current.CancellationToken);

        var maybe = await repository.FindAsync(a => a.Email == "nonexistent@test.com");

        maybe.Should().BeNone();
    }

    #endregion

    #region WhereAsync (predicate) Tests

    [Fact]
    public async Task WhereAsync_Predicate_Returns_Matching_Aggregates()
    {
        var repository = new FakeRepository<TestAggregate, string>();
        await repository.SaveAsync(TestAggregate.Create("1", "Alice"), TestContext.Current.CancellationToken);
        await repository.SaveAsync(TestAggregate.Create("2", "Bob"), TestContext.Current.CancellationToken);
        await repository.SaveAsync(TestAggregate.Create("3", "Alice2"), TestContext.Current.CancellationToken);

        var results = await repository.WhereAsync(a => a.Name.StartsWith("Alice", StringComparison.Ordinal));

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task WhereAsync_Predicate_Returns_Empty_When_No_Match()
    {
        var repository = new FakeRepository<TestAggregate, string>();
        await repository.SaveAsync(TestAggregate.Create("1", "Alice"), TestContext.Current.CancellationToken);

        var results = await repository.WhereAsync(a => a.Name == "Nobody");

        results.Should().BeEmpty();
    }

    #endregion

    #region WhereAsync (Specification) Tests

    [Fact]
    public async Task WhereAsync_Specification_Returns_Matching_Aggregates()
    {
        var repository = new FakeRepository<TestAggregate, string>();
        await repository.SaveAsync(TestAggregate.Create("1", "Alice"), TestContext.Current.CancellationToken);
        await repository.SaveAsync(TestAggregate.Create("2", "Bob"), TestContext.Current.CancellationToken);
        await repository.SaveAsync(TestAggregate.Create("3", "Alice2"), TestContext.Current.CancellationToken);

        var spec = new NameStartsWithSpecification("Alice");
        var results = await repository.WhereAsync(spec);

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task WhereAsync_Specification_Returns_Empty_When_No_Match()
    {
        var repository = new FakeRepository<TestAggregate, string>();
        await repository.SaveAsync(TestAggregate.Create("1", "Alice"), TestContext.Current.CancellationToken);

        var spec = new NameStartsWithSpecification("Nobody");
        var results = await repository.WhereAsync(spec);

        results.Should().BeEmpty();
    }

    private class NameStartsWithSpecification : Specification<TestAggregate>
    {
        private readonly string _prefix;
        public NameStartsWithSpecification(string prefix) => _prefix = prefix;
        public override System.Linq.Expressions.Expression<Func<TestAggregate, bool>> ToExpression() =>
            a => a.Name.StartsWith(_prefix, System.StringComparison.Ordinal);
    }

    #endregion
}