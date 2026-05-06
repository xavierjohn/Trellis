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

    #region Round-N inspection findings (m-T-2, m-T-3, N-T-1)

    [Fact]
    public async Task SaveAsync_Should_Throw_ArgumentNullException_For_Null()
    {
        // Inspection finding m-T-2: Add and Remove already null-guard the aggregate
        // parameter; SaveAsync was missing the same guard. A null caller previously
        // got an opaque NullReferenceException at `aggregate.Id` instead of fail-fast
        // ArgumentNullException with the parameter name.
        var repository = new FakeRepository<TestAggregate, string>();

        var act = async () => await repository.SaveAsync(null!, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("aggregate");
    }

    [Fact]
    public void Remove_Should_Capture_Domain_Events_Before_Removing()
    {
        // Inspection finding m-T-3: Remove() previously dropped the aggregate's
        // UncommittedEvents() — domain events raised on an aggregate before deletion
        // (e.g. OrderCancelled, CustomerArchived) were silently lost. EF's
        // RepositoryBase/SaveChanges flow captures these events at commit time;
        // the fake should mirror that. Add already captures + AcceptChanges; Remove
        // and DeleteAsync now do the same.
        var repository = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Doomed");
        repository.Add(aggregate);
        // Arrange: Add already published the creation event. Now raise an explicit
        // pre-deletion event by calling a domain method.
        aggregate.UpdateName("Final");
        var preRemovalEventCount = repository.PublishedEvents.Count;
        aggregate.UncommittedEvents().Should().NotBeEmpty(
            "the test arranges UpdateName to raise an event so Remove has something to capture");

        repository.Remove(aggregate);

        repository.PublishedEvents.Count.Should().BeGreaterThan(preRemovalEventCount,
            "Remove must capture the aggregate's UncommittedEvents before removing it from the store");
        aggregate.UncommittedEvents().Should().BeEmpty("Remove must AcceptChanges on the aggregate");
    }

    [Fact]
    public void Remove_Untracked_Aggregate_Is_NoOp_Does_Not_Publish_Events()
    {
        // Inspection finding (pre-commit GPT-5.5): when m-T-3 added event capture to
        // Remove, the unconditional `_publishedEvents.AddRange + AcceptChanges`
        // accidentally broke the documented "Remove of an untracked aggregate is a
        // no-op" contract — events of an untracked aggregate would be published and
        // its UncommittedEvents() cleared. The guard `if (_store.ContainsKey(id))`
        // restores the no-op semantics for the untracked path.
        var repository = new FakeRepository<TestAggregate, string>();
        var untrackedAggregate = TestAggregate.Create("never-added", "Detached");
        var preRemovalEventCount = repository.PublishedEvents.Count;
        var preRemovalUncommitted = untrackedAggregate.UncommittedEvents().ToList();
        preRemovalUncommitted.Should().NotBeEmpty(
            "the test arranges TestAggregate.Create to raise a TestEvent so we can verify it is NOT published");

        repository.Remove(untrackedAggregate);

        repository.PublishedEvents.Count.Should().Be(preRemovalEventCount,
            "Remove must NOT publish events for an aggregate that is not tracked by the fake");
        untrackedAggregate.UncommittedEvents().Should().BeEquivalentTo(preRemovalUncommitted,
            "Remove must NOT call AcceptChanges on an untracked aggregate");
        repository.Count.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_Should_Capture_Domain_Events_Before_Removing()
    {
        // Inspection finding m-T-3: same issue as Remove — DeleteAsync(id) used
        // _store.Remove(id) without ever accessing the aggregate, so pre-deletion
        // domain events were lost. Now looks up the aggregate first, captures
        // events + AcceptChanges, then removes.
        var repository = new FakeRepository<TestAggregate, string>();
        var aggregate = TestAggregate.Create("1", "Doomed");
        repository.Add(aggregate);
        aggregate.UpdateName("Final");
        var preRemovalEventCount = repository.PublishedEvents.Count;

        var result = await repository.DeleteAsync(aggregate.Id, TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        repository.PublishedEvents.Count.Should().BeGreaterThan(preRemovalEventCount,
            "DeleteAsync must capture the aggregate's UncommittedEvents before removing it");
        aggregate.UncommittedEvents().Should().BeEmpty("DeleteAsync must AcceptChanges on the aggregate");
    }

    [Fact]
    public async Task QueryAsync_With_Specification_Returns_Matching_Aggregates()
    {
        // Inspection finding N-T-1: FakeRepository now mirrors RepositoryBase's read
        // surface (QueryAsync, ExistsAsync, CountAsync) so test repository adapters
        // built from the RepositoryBase contract work directly against the fake.
        var repository = new FakeRepository<TestAggregate, string>();
        repository.Add(TestAggregate.Create("1", "Apple"));
        repository.Add(TestAggregate.Create("2", "Avocado"));
        repository.Add(TestAggregate.Create("3", "Banana"));

        var results = await repository.QueryAsync(
            new NameStartsWithSpecification("A"),
            TestContext.Current.CancellationToken);

        results.Should().HaveCount(2);
        results.Select(a => a.Name).Should().BeEquivalentTo(["Apple", "Avocado"]);
    }

    [Fact]
    public async Task ExistsAsync_With_Id_Returns_True_When_Aggregate_Exists()
    {
        var repository = new FakeRepository<TestAggregate, string>();
        repository.Add(TestAggregate.Create("1", "Apple"));

        var exists = await repository.ExistsAsync("1", TestContext.Current.CancellationToken);
        var missing = await repository.ExistsAsync("99", TestContext.Current.CancellationToken);

        exists.Should().BeTrue();
        missing.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_With_Specification_Returns_True_When_Any_Match()
    {
        var repository = new FakeRepository<TestAggregate, string>();
        repository.Add(TestAggregate.Create("1", "Apple"));
        repository.Add(TestAggregate.Create("2", "Banana"));

        var anyA = await repository.ExistsAsync(
            new NameStartsWithSpecification("A"),
            TestContext.Current.CancellationToken);
        var anyZ = await repository.ExistsAsync(
            new NameStartsWithSpecification("Z"),
            TestContext.Current.CancellationToken);

        anyA.Should().BeTrue();
        anyZ.Should().BeFalse();
    }

    [Fact]
    public async Task CountAsync_With_Specification_Returns_Match_Count()
    {
        var repository = new FakeRepository<TestAggregate, string>();
        repository.Add(TestAggregate.Create("1", "Apple"));
        repository.Add(TestAggregate.Create("2", "Avocado"));
        repository.Add(TestAggregate.Create("3", "Banana"));

        var countA = await repository.CountAsync(
            new NameStartsWithSpecification("A"),
            TestContext.Current.CancellationToken);
        var countZ = await repository.CountAsync(
            new NameStartsWithSpecification("Z"),
            TestContext.Current.CancellationToken);

        countA.Should().Be(2);
        countZ.Should().Be(0);
    }

    #endregion
}