namespace Trellis.Core.Tests.Results.Extensions.Traverse;

using System.Globalization;
using Trellis.Testing;

public class TraverseTests : TestBase
{
    #region Traverse Sync

    [Fact]
    public void Traverse_EmptyCollection_ReturnsEmptySuccess()
    {
        // Arrange
        var items = Array.Empty<int>();

        // Act
        var result = items.Traverse(x => Result.Ok(x * 2));

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().BeEmpty();
    }

    [Fact]
    public void Traverse_AllItemsSucceed_ReturnsAllTransformed()
    {
        // Arrange
        var items = new[] { 1, 2, 3, 4, 5 };

        // Act
        var result = items.Traverse(x => Result.Ok(x * 2));

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().Equal(2, 4, 6, 8, 10);
    }

    [Fact]
    public void Traverse_FirstItemFails_ReturnsFirstFailure()
    {
        // Arrange
        var items = new[] { 1, 2, 3, 4, 5 };

        // Act
        var result = items.Traverse(x =>
            x == 1 ? Result.Fail<int>(Error1) : Result.Ok(x * 2));

        // Assert
        result.Should().BeFailure()
            .Which.Should().HaveCode(Error1.Code);
    }

    [Fact]
    public void Traverse_MiddleItemFails_ReturnsFailureAndStopsProcessing()
    {
        // Arrange
        var items = new[] { 1, 2, 3, 4, 5 };
        var processedItems = new List<int>();

        // Act
        var result = items.Traverse(x =>
        {
            processedItems.Add(x);
            return x == 3
                ? Result.Fail<int>(Error2)
                : Result.Ok(x * 2);
        });

        // Assert
        result.Should().BeFailure()
            .Which.Should().HaveCode(Error2.Code);
        processedItems.Should().Equal(1, 2, 3); // Short-circuits after failure
    }

    [Fact]
    public void Traverse_LastItemFails_ReturnsFailure()
    {
        // Arrange
        var items = new[] { 1, 2, 3, 4, 5 };

        // Act
        var result = items.Traverse(x =>
            x == 5 ? Result.Fail<int>(Error1) : Result.Ok(x * 2));

        // Assert
        result.Should().BeFailure();
    }

    [Fact]
    public void Traverse_TransformToComplexType_Succeeds()
    {
        // Arrange
        var items = new[] { "apple", "banana", "cherry" };

        // Act
        var result = items.Traverse(s => Result.Ok(new Fruit { Name = s, Length = s.Length }));

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().HaveCount(3);
        result.Unwrap()[0].Name.Should().Be("apple");
    }

    [Fact]
    public void Traverse_WithValidation_FiltersInvalidItems()
    {
        // Arrange
        var items = new[] { "a", "ab", "abc", "abcd", "abcde" };

        // Act
        var result = items.Traverse(s =>
            s.Length >= 3
                ? Result.Ok(s.ToUpper(CultureInfo.InvariantCulture))
                : Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"String too short: {s}" }));

        // Assert
        result.Should().BeFailure();
        result.Error!.Detail.Should().Contain("String too short: a");
    }

    #endregion

    #region TraverseAsync with Task

    [Fact]
    public async Task TraverseAsync_EmptyCollection_ReturnsEmptySuccess()
    {
        // Arrange
        var items = Array.Empty<int>();

        // Act
        var result = await items.TraverseAsync((int x) => Task.FromResult(Result.Ok(x * 2)));

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().BeEmpty();
    }

    [Fact]
    public async Task TraverseAsync_AllItemsSucceed_ReturnsAllTransformed()
    {
        // Arrange
        var items = new[] { 1, 2, 3, 4, 5 };

        // Act
        var result = await items.TraverseAsync((int x) => Task.FromResult(Result.Ok(x * 2)));

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().Equal(2, 4, 6, 8, 10);
    }

    [Fact]
    public async Task TraverseAsync_FirstItemFails_ReturnsFirstFailure()
    {
        // Arrange
        var items = new[] { 1, 2, 3, 4, 5 };

        // Act
        var result = await items.TraverseAsync((int x) =>
            Task.FromResult(x == 1 ? Result.Fail<int>(Error1) : Result.Ok(x * 2)));

        // Assert
        result.Should().BeFailure()
            .Which.Should().HaveCode(Error1.Code);
    }

    [Fact]
    public async Task TraverseAsync_MiddleItemFails_ShortCircuits()
    {
        // Arrange
        var items = new[] { 1, 2, 3, 4, 5 };
        var processedItems = new List<int>();

        // Act
        var result = await items.TraverseAsync((int x) =>
        {
            processedItems.Add(x);
            return Task.FromResult(x == 3
                ? Result.Fail<int>(Error2)
                : Result.Ok(x * 2));
        });

        // Assert
        result.Should().BeFailure();
        processedItems.Should().Equal(1, 2, 3);
    }

    #endregion

    #region TraverseAsync with CancellationToken

    [Fact]
    public async Task TraverseAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var items = new[] { 1, 2, 3, 4, 5 };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => items.TraverseAsync(
                (int x, CancellationToken ct) => Task.FromResult(Result.Ok(x * 2)),
                cts.Token));
    }

    [Fact]
    public async Task TraverseAsync_CancellationDuringProcessing_StopsImmediately()
    {
        // Arrange
        var items = new[] { 1, 2, 3, 4, 5 };
        using var cts = new CancellationTokenSource();
        var processedItems = new List<int>();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => items.TraverseAsync((int x, CancellationToken ct) =>
            {
                processedItems.Add(x);
                if (x == 3) cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(Result.Ok(x * 2));
            }, cts.Token));

        processedItems.Should().BeEquivalentTo(new List<int> { 1, 2, 3 });
    }

    #endregion

    #region TraverseAsync with ValueTask

    [Fact]
    public async Task TraverseAsync_ValueTask_AllItemsSucceed()
    {
        // Arrange
        var items = new[] { 1, 2, 3, 4, 5 };

        // Act
        var result = await items.TraverseAsync((int x) => ValueTask.FromResult(Result.Ok(x * 2)));

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().Equal(2, 4, 6, 8, 10);
    }

    [Fact]
    public async Task TraverseAsync_ValueTask_FirstItemFails()
    {
        // Arrange
        var items = new[] { 1, 2, 3, 4, 5 };

        // Act
        var result = await items.TraverseAsync((int x) =>
            ValueTask.FromResult(x == 1
                ? Result.Fail<int>(Error1)
                : Result.Ok(x * 2)));

        // Assert
        result.Should().BeFailure()
            .Which.Should().HaveCode(Error1.Code);
    }

    [Fact]
    public async Task TraverseAsync_ValueTask_WithCancellationToken_Succeeds()
    {
        // Arrange
        var items = new[] { 1, 2, 3 };
        using var cts = new CancellationTokenSource();

        // Act
        var result = await items.TraverseAsync(
            (int x, CancellationToken ct) => ValueTask.FromResult(Result.Ok(x * 2)),
            cts.Token);

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().Equal(2, 4, 6);
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public void Traverse_ValidateMultipleEmails_AllValid()
    {
        // Arrange
        var emails = new[] { "user1@test.com", "user2@test.com", "user3@test.com" };

        // Act
        var result = emails.Traverse(email =>
            email.Contains('@')
                ? Result.Ok(email.ToLower(CultureInfo.InvariantCulture))
                : Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Invalid email: {email}" }));

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().AllSatisfy(e => e.Should().Contain("@"));
    }

    [Fact]
    public void Traverse_ValidateMultipleEmails_OneInvalid()
    {
        // Arrange
        var emails = new[] { "user1@test.com", "invalid-email", "user3@test.com" };

        // Act
        var result = emails.Traverse(email =>
            email.Contains('@')
                ? Result.Ok(email.ToLower(CultureInfo.InvariantCulture))
                : Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Invalid email: {email}" }));

        // Assert
        result.Should().BeFailure();
        result.Error!.Detail.Should().Contain("invalid-email");
    }

    [Fact]
    public async Task TraverseAsync_FetchMultipleUsers_AllFound()
    {
        // Arrange
        var userIds = new[] { 1, 2, 3 };

        // Act
        var result = await userIds.TraverseAsync((int id) =>
            Task.FromResult(Result.Ok($"User{id}")));

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().Equal("User1", "User2", "User3");
    }

    [Fact]
    public async Task TraverseAsync_FetchMultipleUsers_OneNotFound()
    {
        // Arrange
        var userIds = new[] { 1, 2, 999, 4 };

        // Act
        var result = await userIds.TraverseAsync((int id) =>
            Task.FromResult(id == 999
                ? Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = $"User {id} not found" })
                : Result.Ok($"User{id}")));

        // Assert
        result.Should().BeFailure();
        result.Error!.Should().BeOfType<Error.NotFound>();
        result.Error!.Detail.Should().Contain("999");
    }

    [Fact]
    public async Task TraverseAsync_LargeCollection_ProcessesAll()
    {
        // Arrange
        var items = Enumerable.Range(1, 100).ToArray();

        // Act
        var result = await items.TraverseAsync((int x) =>
            Task.FromResult(Result.Ok(x * 2)));

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().HaveCount(100);
        result.Unwrap().Should().Equal(Enumerable.Range(1, 100).Select(x => x * 2));
    }

    [Fact]
    public void Traverse_TransformStringsToValueObjects_AllValid()
    {
        // Arrange
        var names = new[] { "Alice", "Bob", "Charlie" };

        // Act
        var result = names.Traverse(name =>
            !string.IsNullOrWhiteSpace(name)
                ? Result.Ok(new Name(name))
                : Result.Fail<Name>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Name cannot be empty" }));

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().HaveCount(3);
        result.Unwrap().Should().AllSatisfy(n => n.Value.Should().NotBeNullOrEmpty());
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Traverse_SingleItem_Success()
    {
        // Arrange
        var items = new[] { 42 };

        // Act
        var result = items.Traverse(x => Result.Ok(x * 2));

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().ContainSingle()
            .Which.Should().Be(84);
    }

    [Fact]
    public void Traverse_SingleItem_Failure()
    {
        // Arrange
        var items = new[] { 42 };

        // Act
        var result = items.Traverse(x => Result.Fail<int>(Error1));

        // Assert
        result.Should().BeFailure();
    }

    [Fact]
    public async Task TraverseAsync_NullCollection_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<int>? items = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => items!.TraverseAsync((int x) => Task.FromResult(Result.Ok(x * 2))));
    }

    [Fact]
    public void Traverse_MaintainsOrder_WhenAllSucceed()
    {
        // Arrange
        var items = new[] { 5, 3, 8, 1, 9, 2 };

        // Act
        var result = items.Traverse(x => Result.Ok(x * 10));

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().Equal(50, 30, 80, 10, 90, 20);
    }

    #endregion

    #region Sequence

    [Fact]
    public void Sequence_EmptyCollection_ReturnsEmptySuccess()
    {
        var results = Array.Empty<Result<int>>();

        var result = results.Sequence();

        result.Should().BeSuccess();
        result.Unwrap().Should().BeEmpty();
    }

    [Fact]
    public void Sequence_AllSucceed_ReturnsAllValuesInOrder()
    {
        var results = new[] { Result.Ok(1), Result.Ok(2), Result.Ok(3) };

        var result = results.Sequence();

        result.Should().BeSuccess();
        result.Unwrap().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Sequence_FirstFails_ReturnsFirstFailure()
    {
        var results = new[] { Result.Fail<int>(Error1), Result.Ok(2), Result.Fail<int>(Error2) };

        var result = results.Sequence();

        result.Should().BeFailure().Which.Should().HaveCode(Error1.Code);
    }

    [Fact]
    public void Sequence_MiddleFails_StopsAtFirstFailure()
    {
        var enumerated = new List<int>();
        IEnumerable<Result<int>> Gen()
        {
            foreach (var x in new[] { 1, 2, 3, 4, 5 })
            {
                enumerated.Add(x);
                yield return x == 3 ? Result.Fail<int>(Error1) : Result.Ok(x);
            }
        }

        var result = Gen().Sequence();

        result.Should().BeFailure().Which.Should().HaveCode(Error1.Code);
        enumerated.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Sequence_NullSource_ThrowsArgumentNullException()
    {
        IEnumerable<Result<int>>? source = null;

        var act = () => source!.Sequence();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Sequence_Composes_With_Select_For_PerItem_Compute()
    {
        // Mirrors Sonnet smoke-lab #3 use case: per-line-item subtotals as Result<T>,
        // then aggregate. Sequence lifts IEnumerable<Result<T>> to Result<IReadOnlyList<T>>.
        var quantities = new[] { 2, 5, 0, 3 };

        Result<int> Subtotal(int q) =>
            q > 0 ? Result.Ok(q * 10) : Result.Fail<int>(Error1);

        var result = quantities.Select(Subtotal).Sequence();

        result.Should().BeFailure().Which.Should().HaveCode(Error1.Code);
    }

    [Fact]
    public void Sequence_Unit_EmptyCollection_ReturnsSuccess()
    {
        var results = Array.Empty<Result<Unit>>();

        var result = results.Sequence();

        result.Should().BeSuccess();
    }

    [Fact]
    public void Sequence_Unit_AllSucceed_ReturnsSuccess()
    {
        var results = new[] { Result.Ok(), Result.Ok(), Result.Ok() };

        var result = results.Sequence();

        result.Should().BeSuccess();
    }

    [Fact]
    public void Sequence_Unit_FirstFails_ReturnsFirstFailure()
    {
        var results = new[] { Result.Ok(), Result.Fail(Error1), Result.Fail(Error2) };

        var result = results.Sequence();

        result.Should().BeFailure().Which.Should().HaveCode(Error1.Code);
    }

    [Fact]
    public void Sequence_Unit_NullSource_ThrowsArgumentNullException()
    {
        IEnumerable<Result<Unit>>? source = null;

        var act = () => source!.Sequence();

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    // Helper classes
    private class Fruit
    {
        public string Name { get; set; } = string.Empty;
        public int Length { get; set; }
    }

    private record Name(string Value);
}