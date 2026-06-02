namespace Trellis.Testing.Tests;

public class FakeRepositoryParityTests
{
    [Fact]
    public async Task FakeRepository_DuplicateKey_UsesSameReasonCodeAsRealEfRuntime()
    {
        // Canonical EF runtime reason code per Trellis.EntityFrameworkCore.DbContextExtensions.
        const string canonicalDuplicateKeyCode = "duplicate.key";

        var fake = new FakeRepository<TestAggregate, string>()
            .WithUniqueConstraint(aggregate => aggregate.Email);
        await fake.SaveAsync(TestAggregate.Create("existing", "same@example.com"), TestContext.Current.CancellationToken);

        var conflict = await fake.SaveAsync(TestAggregate.Create("duplicate", "same@example.com"), TestContext.Current.CancellationToken);

        conflict.IsFailure.Should().BeTrue();
        conflict.UnwrapError().Should().BeOfType<Error.Conflict>()
            .Which.ReasonCode.Should().Be(canonicalDuplicateKeyCode);
    }

    [Fact]
    public async Task FakeRepository_NotFound_UsesSameDetailMessageShapeAsRealEfRuntime()
    {
        var fake = new FakeRepository<TestAggregate, string>();
        const string id = "missing";
        var expectedDetail = $"{ResourceRef.FormatTypeName(typeof(TestAggregate))} with ID '{id}' not found.";

        var getResult = await fake.GetByIdAsync(id, TestContext.Current.CancellationToken);
        var deleteResult = await fake.DeleteAsync(id, TestContext.Current.CancellationToken);

        getResult.IsFailure.Should().BeTrue();
        getResult.UnwrapError().Should().BeOfType<Error.NotFound>()
            .Which.Detail.Should().Be(expectedDetail);
        deleteResult.IsFailure.Should().BeTrue();
        deleteResult.UnwrapError().Should().BeOfType<Error.NotFound>()
            .Which.Detail.Should().Be(expectedDetail);
    }

    private sealed class TestAggregate : Aggregate<string>
    {
        private TestAggregate(string id, string email) : base(id) => Email = email;

        public string Email { get; }

        public static TestAggregate Create(string id, string email) => new(id, email);
    }
}
