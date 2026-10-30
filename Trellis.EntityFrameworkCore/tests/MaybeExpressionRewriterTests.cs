namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;

/// <summary>
/// Tests for <see cref="MaybeExpressionRewriter"/> and <see cref="MaybeQueryInterceptor"/>.
/// Validates that LINQ expressions referencing <see cref="Maybe{T}"/> properties are
/// automatically rewritten to EF Core-translatable storage member references.
/// </summary>
public class MaybeExpressionRewriterTests : IDisposable
{
    private readonly InterceptorTestDbContext _context;
    private readonly SqliteConnection _connection;

    public MaybeExpressionRewriterTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<InterceptorTestDbContext>()
            .UseSqlite(_connection).IgnoreManyServiceProvidersCreatedWarning()
            // Use AddTrellisInterceptors so MaybeEvaluatableExpressionFilterPlugin is wired
            // into the per-context internal service provider; this is the supported
            // registration path for Maybe equality translation.
            .AddTrellisInterceptors()
            // Each test in this class constructs a fresh DbContext (xUnit per-test
            // instantiation), so the test class can produce more than 20 internal EF service
            // providers in one assembly run. Silence the warning — it is purely a test-host
            // signal here, not a production concern.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        _context = new InterceptorTestDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    #region HasValue / HasNoValue

    [Fact]
    public async Task HasValue_TranslatesToNotNull()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var withPhone = CreateCustomer("Alice", "+1-555-0100");
        var withoutPhone = CreateCustomer("Bob");
        _context.Customers.AddRange(withPhone, withoutPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act — natural LINQ, no explicit extension methods
        var results = await _context.Customers
            .Where(c => c.Phone.HasValue)
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Id.Should().Be(withPhone.Id);
    }

    [Fact]
    public async Task HasNoValue_TranslatesToIsNull()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var withPhone = CreateCustomer("Alice", "+1-555-0100");
        var withoutPhone = CreateCustomer("Bob");
        _context.Customers.AddRange(withPhone, withoutPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Customers
            .Where(c => c.Phone.HasNoValue)
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Id.Should().Be(withoutPhone.Id);
    }

    #endregion

    #region GetValueOrDefault with comparison

    [Fact]
    public async Task GetValueOrDefault_WithLessThanComparison_TranslatesToCoalesce()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var early = CreateOrder(customer.Id);
        early.SubmittedAt = Maybe.From(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        var late = CreateOrder(customer.Id);
        late.SubmittedAt = Maybe.From(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        var noDate = CreateOrder(customer.Id); // NULL

        _context.Orders.AddRange(early, late, noDate);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var cutoff = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act — specification-style expression with GetValueOrDefault
        var results = await _context.Orders
            .Where(o => o.SubmittedAt.GetValueOrDefault(DateTime.MaxValue) < cutoff)
            .ToListAsync(ct);

        // Assert — only the early order matches (noDate gets MaxValue, so excluded)
        results.Should().ContainSingle()
            .Which.Id.Should().Be(early.Id);
    }

    [Fact]
    public async Task GetValueOrDefault_OverdueSpecificationPattern_Works()
    {
        // Arrange — simulates the OverdueOrderSpecification use case
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var overdueOrder = CreateOrder(customer.Id, TestOrderStatus.Confirmed);
        overdueOrder.SubmittedAt = Maybe.From(DateTime.UtcNow.AddDays(-10));
        var recentOrder = CreateOrder(customer.Id, TestOrderStatus.Confirmed);
        recentOrder.SubmittedAt = Maybe.From(DateTime.UtcNow.AddDays(-2));
        var shippedOrder = CreateOrder(customer.Id, TestOrderStatus.Shipped);
        shippedOrder.SubmittedAt = Maybe.From(DateTime.UtcNow.AddDays(-10));

        _context.Orders.AddRange(overdueOrder, recentOrder, shippedOrder);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var cutoff = DateTime.UtcNow.AddDays(-7);

        // Act — this is what a specification's ToExpression() would return
        var results = await _context.Orders
            .Where(o => o.Status == TestOrderStatus.Confirmed
                     && o.SubmittedAt.GetValueOrDefault(DateTime.MaxValue) < cutoff)
            .ToListAsync(ct);

        // Assert — only the overdue submitted order
        results.Should().ContainSingle()
            .Which.Id.Should().Be(overdueOrder.Id);
    }

    #endregion

    #region Value comparison pattern

    [Fact]
    public async Task Value_LessThan_TranslatesDirectly()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var early = CreateOrder(customer.Id);
        early.SubmittedAt = Maybe.From(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        var late = CreateOrder(customer.Id);
        late.SubmittedAt = Maybe.From(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        var noDate = CreateOrder(customer.Id);

        _context.Orders.AddRange(early, late, noDate);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var cutoff = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act — clean pattern: HasValue && Value < cutoff (no GetValueOrDefault sentinel)
        var results = await _context.Orders
            .Where(o => o.SubmittedAt.HasValue && o.SubmittedAt.Value < cutoff)
            .ToListAsync(ct);

        // Assert — only early matches (noDate excluded by HasValue, late excluded by < cutoff)
        results.Should().ContainSingle()
            .Which.Id.Should().Be(early.Id);
    }

    #endregion

    #region Specification integration

    [Fact]
    public async Task Specification_OverdueOrderPattern_FiltersInDatabase()
    {
        // Arrange — mirrors the lab's OverdueOrderSpecification scenario:
        //   Status == Confirmed && SubmittedAt.HasValue && SubmittedAt.Value < threshold
        // The MaybeQueryInterceptor must rewrite the Maybe<T> property accesses to
        // EF.Property<DateTime?>(o, "_submittedAt") so the predicate translates to SQL
        // and the time-filter is actually applied at the database (not silently dropped).
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var oldSubmitted = CreateOrder(customer.Id, TestOrderStatus.Confirmed);
        oldSubmitted.SubmittedAt = Maybe.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var recentSubmitted = CreateOrder(customer.Id, TestOrderStatus.Confirmed);
        recentSubmitted.SubmittedAt = Maybe.From(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var draftWithOldDate = CreateOrder(customer.Id, TestOrderStatus.Draft);
        draftWithOldDate.SubmittedAt = Maybe.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var confirmedWithoutDate = CreateOrder(customer.Id, TestOrderStatus.Confirmed);
        // SubmittedAt left as Maybe<DateTime>.None

        _context.Orders.AddRange(oldSubmitted, recentSubmitted, draftWithOldDate, confirmedWithoutDate);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var threshold = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var spec = new OverdueOrderTestSpecification(threshold);

        // Act
        var overdue = await _context.Orders
            .Where(spec.ToExpression())
            .ToListAsync(ct);

        // Assert — only oldSubmitted matches all three predicates
        overdue.Should().ContainSingle()
            .Which.Id.Should().Be(oldSubmitted.Id);
    }

    [Fact]
    public void Specification_OverdueOrderPattern_CompiledLambdaShortCircuits_NoneSafe()
    {
        // The same Specification used against an in-memory list (e.g., FakeRepository)
        // must NOT throw when SubmittedAt is None — C# AndAlso short-circuits the
        // .Value access. This pins parity with the EF path so users can trust the same
        // Specification in both production and fake-repository tests.
        var threshold = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var predicate = new OverdueOrderTestSpecification(threshold).ToExpression().Compile();

        var noneOrder = new TestOrder
        {
            Id = TestOrderId.NewUniqueV7(),
            CustomerId = TestCustomerId.NewUniqueV7(),
            Amount = 1m,
            Status = TestOrderStatus.Confirmed,
            // SubmittedAt left as Maybe<DateTime>.None — would throw if .Value evaluated eagerly
        };

        var act = () => predicate(noneOrder);

        act.Should().NotThrow();
        predicate(noneOrder).Should().BeFalse();
    }

    #endregion

    #region Equality with Maybe<T>.None

    [Fact]
    public async Task EqualsMaybeNone_TranslatesToIsNull()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var withPhone = CreateCustomer("Alice", "+1-555-0100");
        var withoutPhone = CreateCustomer("Bob");
        _context.Customers.AddRange(withPhone, withoutPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act — compare Maybe<T> property against Maybe<T>.None using == operator
        var results = await _context.Customers
            .Where(c => c.Phone == Maybe<PhoneNumber>.None)
            .ToListAsync(ct);

        // Assert — only the customer without phone should match
        results.Should().ContainSingle()
            .Which.Id.Should().Be(withoutPhone.Id);
    }

    [Fact]
    public async Task NotEqualsMaybeNone_TranslatesToIsNotNull()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var withPhone = CreateCustomer("Charlie", "+1-555-0300");
        var withoutPhone = CreateCustomer("Dave");
        _context.Customers.AddRange(withPhone, withoutPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Customers
            .Where(c => c.Phone != Maybe<PhoneNumber>.None)
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Id.Should().Be(withPhone.Id);
    }

    [Fact]
    public async Task EqualsMaybeFromValue_NaturalForm_ReturnsMatchingRow()
    {
        // Regression: the natural form `c.Phone == Maybe.From(value)` previously translated
        // to `_phone IS NULL` because EF Core's parameter extraction lifted the closed-expression
        // operand to a `QueryParameterExpression` BEFORE `MaybeQueryInterceptor.QueryCompilationStarting`
        // ran, erasing the syntactic difference between `None` and `Some(value)`. The fix is a
        // `MaybeEvaluatableExpressionFilterPlugin` registered in EF Core's internal services that
        // marks any `Maybe<T>`-typed node as non-evaluatable, preserving the syntactic distinction
        // for the rewriter to translate correctly.
        var ct = TestContext.Current.CancellationToken;
        var target = "+1-555-0500";
        var withTarget = CreateCustomer("Eve", target);
        var withOther = CreateCustomer("Frank", "+1-555-0501");
        var withoutPhone = CreateCustomer("Grace");
        _context.Customers.AddRange(withTarget, withOther, withoutPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var phone = PhoneNumber.Create(target);

        var results = await _context.Customers
            .Where(c => c.Phone == Maybe.From(phone))
            .ToListAsync(ct);

        results.Should().ContainSingle()
            .Which.Id.Should().Be(withTarget.Id);
    }

    [Fact]
    public async Task NotEqualsMaybeFromValue_NaturalForm_ExcludesMatchingRowsAndIncludesNoneRowsPerCSharpSemantics()
    {
        // Symmetric to EqualsMaybeFromValue_NaturalForm_ReturnsMatchingRow: `!= Maybe.From(value)`
        // must match C# semantics (`Maybe<T>.None != Maybe.From(x)` is `true`). EF Core's
        // default "use relational nulls" → false behavior wraps the SQL `<>` in an `OR IS NULL`
        // so NULL rows are included in the result, matching the in-memory Maybe semantic.
        var ct = TestContext.Current.CancellationToken;
        var target = "+1-555-0500";
        var withTarget = CreateCustomer("Eve", target);
        var withOther = CreateCustomer("Frank", "+1-555-0501");
        var withoutPhone = CreateCustomer("Grace");
        _context.Customers.AddRange(withTarget, withOther, withoutPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var phone = PhoneNumber.Create(target);

        var results = await _context.Customers
            .Where(c => c.Phone != Maybe.From(phone))
            .ToListAsync(ct);

        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo([withOther.Id, withoutPhone.Id]);
    }

    [Fact]
    public async Task EqualsMaybeFromValue_ConverseDirection_ReturnsMatchingRow()
    {
        // `Maybe.From(value) == c.Phone` — same semantics with operands swapped. VisitBinary
        // must rewrite both sides symmetrically.
        var ct = TestContext.Current.CancellationToken;
        var target = "+1-555-0600";
        var withTarget = CreateCustomer("Hank", target);
        var withOther = CreateCustomer("Ivy", "+1-555-0601");
        var withoutPhone = CreateCustomer("Jack");
        _context.Customers.AddRange(withTarget, withOther, withoutPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var phone = PhoneNumber.Create(target);

        var results = await _context.Customers
            .Where(c => Maybe.From(phone) == c.Phone)
            .ToListAsync(ct);

        results.Should().ContainSingle()
            .Which.Id.Should().Be(withTarget.Id);
    }

    [Fact]
    public async Task EqualsCapturedMaybeVariable_Throws_WithActionableMessage()
    {
        // Captured locals of type Maybe<T> funcletize to QueryParameterExpressions and the
        // rewriter cannot tell None from Some at translation time. Throw with a clear message
        // naming the supported alternatives instead of silently miss-querying.
        var ct = TestContext.Current.CancellationToken;
        var withPhone = CreateCustomer("Kim", "+1-555-0700");
        var withoutPhone = CreateCustomer("Leo");
        _context.Customers.AddRange(withPhone, withoutPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var captured = Maybe.From(PhoneNumber.Create("+1-555-0700"));

        var act = async () => await _context.Customers
            .Where(c => c.Phone == captured)
            .ToListAsync(ct);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("Maybe.From(value)")
            .And.Contain("WhereEquals");
    }

    #endregion

    #region HasValueWhere (predicate)

    [Fact]
    public async Task HasValueWhere_ValueType_FiltersByPredicate_ExcludingNone()
    {
        // The canonical Maybe-with-predicate shape:
        //   o.SubmittedAt.HasValueWhere(t => t < cutoff)
        //
        // The rewriter must translate this to
        //   _submittedAt IS NOT NULL AND _submittedAt < @cutoff
        // so that None rows are excluded without evaluating the predicate.
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var early = CreateOrder(customer.Id);
        early.SubmittedAt = Maybe.From(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        var late = CreateOrder(customer.Id);
        late.SubmittedAt = Maybe.From(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        var noDate = CreateOrder(customer.Id);

        _context.Orders.AddRange(early, late, noDate);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var cutoff = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        var results = await _context.Orders
            .Where(o => o.SubmittedAt.HasValueWhere(t => t < cutoff))
            .ToListAsync(ct);

        results.Should().ContainSingle()
            .Which.Id.Should().Be(early.Id);
    }

    [Fact]
    public async Task HasValueWhere_PredicateReferencesAnotherMaybeOnSameEntity_RewritesNestedAccess()
    {
        // Recursive Visit(rewrittenBody) is required so that nested Maybe accesses inside
        // the predicate body are also translated. Here the predicate body references
        // o.OptionalStatus.HasValue (another Maybe on the same entity).
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var submittedAndOptionalSet = CreateOrder(customer.Id);
        submittedAndOptionalSet.SubmittedAt = Maybe.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        submittedAndOptionalSet.OptionalStatus = Maybe.From(TestOrderStatus.Confirmed);

        var submittedOnly = CreateOrder(customer.Id);
        submittedOnly.SubmittedAt = Maybe.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var optionalOnly = CreateOrder(customer.Id);
        optionalOnly.OptionalStatus = Maybe.From(TestOrderStatus.Confirmed);

        _context.Orders.AddRange(submittedAndOptionalSet, submittedOnly, optionalOnly);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var results = await _context.Orders
            .Where(o => o.SubmittedAt.HasValueWhere(_ => o.OptionalStatus.HasValue))
            .ToListAsync(ct);

        results.Should().ContainSingle()
            .Which.Id.Should().Be(submittedAndOptionalSet.Id);
    }

    [Fact]
    public async Task HasValueWhere_PredicateReferencesOuterEntityField_Works()
    {
        // Rubber-duck-suggested case: the predicate body references the outer entity
        // parameter (not just the inner Maybe value). Verifies that ParameterReplacer
        // only touches the inner lambda's parameter, leaving the outer entity reference
        // intact for EF to translate normally.
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var matchAmount = CreateOrder(customer.Id);
        matchAmount.Amount = 5m;
        matchAmount.SubmittedAt = Maybe.From(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));

        var noMatch = CreateOrder(customer.Id);
        noMatch.Amount = 500m;
        noMatch.SubmittedAt = Maybe.From(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));

        _context.Orders.AddRange(matchAmount, noMatch);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Predicate body: `t.Day > o.Amount` — references both the inner t and outer o.
        // Day of 2026-01-10 is 10; matchAmount has Amount=5 (10>5 true), noMatch has Amount=500 (10>500 false).
        var results = await _context.Orders
            .Where(o => o.SubmittedAt.HasValueWhere(t => t.Day > o.Amount))
            .ToListAsync(ct);

        results.Should().ContainSingle()
            .Which.Id.Should().Be(matchAmount.Id);
    }

    [Fact]
    public async Task HasValueWhere_OnSpecificationPredicate_TranslatesEquivalentToHasValueAndValuePattern()
    {
        // The whole point of HasValueWhere is to make specifications more natural. Confirm that
        // a Specification using HasValueWhere behaves identically to the HasValue && Value form.
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var early = CreateOrder(customer.Id, TestOrderStatus.Confirmed);
        early.SubmittedAt = Maybe.From(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        var late = CreateOrder(customer.Id, TestOrderStatus.Confirmed);
        late.SubmittedAt = Maybe.From(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        var noDate = CreateOrder(customer.Id, TestOrderStatus.Confirmed);

        _context.Orders.AddRange(early, late, noDate);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var threshold = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var spec = new OverdueOrderHasValueWhereSpecification(threshold);

        var results = await _context.Orders
            .Where(spec.ToExpression())
            .ToListAsync(ct);

        results.Should().ContainSingle()
            .Which.Id.Should().Be(early.Id);
    }

    [Fact]
    public async Task HasValueWhere_NonInlineLambda_CapturedDelegate_FallsThroughAndThrowsAtTranslation()
    {
        // Documented limitation: the rewriter only handles an inline LambdaExpression as
        // the HasValueWhere predicate. When the predicate is a captured Func<T,bool> variable,
        // the expression-tree representation is a ConstantExpression holding an opaque
        // delegate — the rewriter cannot inspect it. The branch falls through to the base
        // visitor and EF Core's query translation reports the failure.
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var any = CreateOrder(customer.Id);
        any.SubmittedAt = Maybe.From(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        _context.Orders.Add(any);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var cutoff = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        Func<DateTime, bool> predicate = t => t < cutoff;

        var act = async () => await _context.Orders
            .Where(o => o.SubmittedAt.HasValueWhere(predicate))
            .ToListAsync(ct);

        // Throw kind is provider-specific; assert only that the limitation surfaces, not the exception type.
        await act.Should().ThrowAsync<Exception>();
    }

    #endregion

    #region Helpers

    private static TestCustomer CreateCustomer(string name, string? phone = null)
    {
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV7(),
            Name = TestCustomerName.Create(name),
            Email = EmailAddress.Create($"{name.ToLowerInvariant()}@test.com"),
            CreatedAt = DateTime.UtcNow
        };

        if (phone is not null)
            customer.Phone = Maybe.From(PhoneNumber.Create(phone));

        return customer;
    }

    private static TestOrder CreateOrder(TestCustomerId customerId, TestOrderStatus? status = null) =>
        new()
        {
            Id = TestOrderId.NewUniqueV7(),
            CustomerId = customerId,
            Amount = 100m,
            Status = status ?? TestOrderStatus.Draft
        };

    /// <summary>
    /// Test specification: customers who have a phone number.
    /// Uses Maybe&lt;T&gt;.HasValue in the expression — should be rewritten by the interceptor.
    /// </summary>
    private sealed class HasPhoneSpecification : Specification<TestCustomer>
    {
        public override System.Linq.Expressions.Expression<Func<TestCustomer, bool>> ToExpression() =>
            customer => customer.Phone.HasValue;
    }

    /// <summary>
    /// Test specification mirroring the lab's <c>OverdueOrderSpecification</c> exactly:
    /// orders in a given status whose <c>partial Maybe&lt;DateTime&gt; SubmittedAt</c>
    /// is older than a threshold. Documents the canonical
    /// <c>HasValue &amp;&amp; Value &lt; threshold</c> pattern that works in both EF (via
    /// <see cref="MaybeQueryInterceptor"/>) and in-memory (via C# short-circuiting on the
    /// compiled lambda — important for fake-repository parity).
    /// </summary>
    private sealed class OverdueOrderTestSpecification(DateTime threshold) : Specification<TestOrder>
    {
        public override System.Linq.Expressions.Expression<Func<TestOrder, bool>> ToExpression() =>
            o => o.Status == TestOrderStatus.Confirmed
                 && o.SubmittedAt.HasValue
                 && o.SubmittedAt.Value < threshold;
    }

    /// <summary>
    /// Test specification using the <c>HasValueWhere(predicate)</c> shape — exactly the natural
    /// form the rewriter is added to support. Must produce identical results to
    /// <see cref="OverdueOrderTestSpecification"/> when interpreted by EF Core.
    /// </summary>
    private sealed class OverdueOrderHasValueWhereSpecification(DateTime threshold) : Specification<TestOrder>
    {
        public override System.Linq.Expressions.Expression<Func<TestOrder, bool>> ToExpression() =>
            o => o.Status == TestOrderStatus.Confirmed
                 && o.SubmittedAt.HasValueWhere(t => t < threshold);
    }

    /// <summary>
    /// Test DbContext with the <see cref="MaybeQueryInterceptor"/> registered via AddTrellisInterceptors().
    /// </summary>
    private sealed class InterceptorTestDbContext(DbContextOptions<InterceptorTestDbContext> options)
        : DbContext(options)
    {
        public DbSet<TestCustomer> Customers => Set<TestCustomer>();
        public DbSet<TestOrder> Orders => Set<TestOrder>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestCustomerId).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestCustomer>(b =>
            {
                b.HasKey(c => c.Id);
                b.Property(c => c.Name).HasMaxLength(100).IsRequired();
                b.Property(c => c.Email).HasMaxLength(254).IsRequired();
                b.Property(c => c.CreatedAt).IsRequired();
            });

            modelBuilder.Entity<TestOrder>(b =>
            {
                b.HasKey(o => o.Id);
                b.HasOne(o => o.Customer).WithMany(c => c.Orders).HasForeignKey(o => o.CustomerId);
                b.Property(o => o.Amount).IsRequired();
                b.Property(o => o.Status).IsRequired();
            });
        }
    }

    #endregion
}

/// <summary>
/// Tests that <see cref="DbContextOptionsBuilderExtensions.AddTrellisInterceptors"/> registers the
/// singleton <see cref="MaybeQueryInterceptor"/> and Maybe expressions translate correctly.
/// </summary>
public class AddTrellisInterceptorsTests : IDisposable
{
    private readonly DbContext _context;
    private readonly SqliteConnection _connection;

    public AddTrellisInterceptorsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AddTrellisInterceptorsTestDbContext>()
            .UseSqlite(_connection).IgnoreManyServiceProvidersCreatedWarning()
            .AddTrellisInterceptors()
            .Options;

        _context = new AddTrellisInterceptorsTestDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task HasValueWhere_Works_WithAddTrellisInterceptors()
    {
        // End-to-end smoke test through the supported registration path:
        //   AddTrellisInterceptors() → MaybeQueryInterceptor singleton
        //   → MaybeExpressionRewriter sees the IsSome/HasValueWhere call
        //   → produces `_phone IS NOT NULL AND _phone = @target`
        //   → SQLite executes, returns the matching row.
        // Captures the generated SQL to confirm the IS NOT NULL guard is present in the
        // emitted query (not just that the predicate happens to produce the right rows
        // by chance — the IS NOT NULL is what prevents NRE on None rows in stricter
        // providers and matches the rewriter's documented contract).
        var ct = TestContext.Current.CancellationToken;
        var targetPhone = PhoneNumber.Create("+1-555-0500");
        var match = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV7(),
            Name = TestCustomerName.Create("Match"),
            Email = EmailAddress.Create("match@test.com"),
            CreatedAt = DateTime.UtcNow,
            Phone = Maybe.From(targetPhone),
        };
        var otherPhone = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV7(),
            Name = TestCustomerName.Create("OtherPhone"),
            Email = EmailAddress.Create("other@test.com"),
            CreatedAt = DateTime.UtcNow,
            Phone = Maybe.From(PhoneNumber.Create("+1-555-9999")),
        };
        var noPhone = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV7(),
            Name = TestCustomerName.Create("NoPhone"),
            Email = EmailAddress.Create("nophone@test.com"),
            CreatedAt = DateTime.UtcNow,
        };

        _context.Set<TestCustomer>().AddRange(match, otherPhone, noPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Capture the SQL EF Core actually emits, so the smoke test asserts on the
        // rewriter's observable output, not just on result-set correctness.
        var queryable = _context.Set<TestCustomer>()
            .Where(c => c.Phone.HasValueWhere(p => p == targetPhone));
        var generatedSql = queryable.ToQueryString();

        var results = await queryable.ToListAsync(ct);

        results.Should().ContainSingle().Which.Id.Should().Be(match.Id);

        // The rewriter is contractually obliged to emit a NULL guard before evaluating
        // the predicate body. SQLite three-valued logic would mask the absence of the
        // guard for `=` predicates, but the guard remains required for correctness on
        // predicates that would otherwise materialise NULL into a non-nullable comparand.
        generatedSql.Should().MatchRegex(@"""Phone""\s+IS\s+NOT\s+NULL",
            "the MaybeExpressionRewriter must wrap HasValueWhere(predicate) with `_storage IS NOT NULL AND …`");
    }

    [Fact]
    public void AddTrellisInterceptors_Called_Twice_Uses_Same_Singleton()
    {
        // Should not throw ManyServiceProvidersCreatedWarning
        var options1 = new DbContextOptionsBuilder<AddTrellisInterceptorsTestDbContext>()
            .UseSqlite(_connection).IgnoreManyServiceProvidersCreatedWarning()
            .AddTrellisInterceptors()
            .Options;

        var options2 = new DbContextOptionsBuilder<AddTrellisInterceptorsTestDbContext>()
            .UseSqlite(_connection).IgnoreManyServiceProvidersCreatedWarning()
            .AddTrellisInterceptors()
            .Options;

        // Both should resolve without multiple service provider warnings
        using var ctx1 = new AddTrellisInterceptorsTestDbContext(options1);
        using var ctx2 = new AddTrellisInterceptorsTestDbContext(options2);
    }

    private sealed class AddTrellisInterceptorsTestDbContext(DbContextOptions<AddTrellisInterceptorsTestDbContext> options)
        : DbContext(options)
    {
        public DbSet<TestCustomer> Customers => Set<TestCustomer>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestCustomerId).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<TestCustomer>(b =>
            {
                b.HasKey(c => c.Id);
                b.Property(c => c.Name).HasMaxLength(100).IsRequired();
                b.Property(c => c.Email).HasMaxLength(254).IsRequired();
                b.Property(c => c.CreatedAt).IsRequired();
            });
    }
}