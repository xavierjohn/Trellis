namespace Trellis.Testing.Tests.Fakes;

public class AggregateTestMutatorTests
{
    #region Test Aggregate

    private class TestOrder : Aggregate<string>
    {
        public string Status { get; private set; } = "Draft";

        // Manually replicate what MaybePartialPropertyGenerator emits
        private DateTime? _submittedAt;
        public Maybe<DateTime> SubmittedAt
        {
            get => _submittedAt is not null ? Maybe.From(_submittedAt.Value) : Maybe<DateTime>.None;
            set => _submittedAt = value.HasValue ? value.Value : null;
        }

        private DateTime? _shippedAt;
        public Maybe<DateTime> ShippedAt
        {
            get => _shippedAt is not null ? Maybe.From(_shippedAt.Value) : Maybe<DateTime>.None;
            set => _shippedAt = value.HasValue ? value.Value : null;
        }

        private TestOrder(string id) : base(id) { }

        public static TestOrder Create(string id) => new(id);

        public void Submit()
        {
            Status = "Submitted";
            SubmittedAt = DateTime.UtcNow;
        }
    }

    #endregion

    #region SetMaybeField Tests

    [Fact]
    public void SetMaybeField_Sets_Backing_Field_Value()
    {
        var order = TestOrder.Create("1");
        order.SubmittedAt.Should().BeNone();

        var pastDate = DateTime.UtcNow.AddDays(-8);
        order.SetMaybeField(o => o.SubmittedAt, pastDate);

        order.SubmittedAt.Should().HaveValue();
        order.SubmittedAt.Value.Should().Be(pastDate);
    }

    [Fact]
    public void SetMaybeField_Clears_Value_Via_ClearMaybeField()
    {
        var order = TestOrder.Create("1");
        order.Submit();
        order.SubmittedAt.Should().HaveValue();

        order.ClearMaybeField(o => o.SubmittedAt);

        order.SubmittedAt.Should().BeNone();
    }

    [Fact]
    public void SetMaybeField_Returns_Entity_For_Fluent_Chaining()
    {
        var order = TestOrder.Create("1");
        var pastDate = DateTime.UtcNow.AddDays(-8);
        var shippedDate = DateTime.UtcNow.AddDays(-5);

        var result = order
            .SetMaybeField(o => o.SubmittedAt, pastDate)
            .SetMaybeField(o => o.ShippedAt, shippedDate);

        result.Should().BeSameAs(order);
        order.SubmittedAt.Should().HaveValueEqualTo(pastDate);
        order.ShippedAt.Should().HaveValueEqualTo(shippedDate);
    }

    [Fact]
    public void SetMaybeField_Throws_When_Backing_Field_Missing()
    {
        var order = TestOrderWithoutBackingField.Create("1");

        var act = () => order.SetMaybeField(o => o.Notes, "hello");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Backing field*not found*");
    }

    // Aggregate with a Maybe<T> property that has NO source-generated backing field
    private class TestOrderWithoutBackingField : Aggregate<string>
    {
        public Maybe<string> Notes { get; set; }
        private TestOrderWithoutBackingField(string id) : base(id) { }
        public static TestOrderWithoutBackingField Create(string id) => new(id);
    }

    #endregion

    #region ClearMaybeField Tests

    [Fact]
    public void ClearMaybeField_Sets_To_None()
    {
        var order = TestOrder.Create("1");
        order.Submit();
        order.SubmittedAt.Should().HaveValue();

        order.ClearMaybeField(o => o.SubmittedAt);

        order.SubmittedAt.Should().BeNone();
    }

    #endregion
}
