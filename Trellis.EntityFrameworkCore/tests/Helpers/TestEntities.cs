namespace Trellis.EntityFrameworkCore.Tests.Helpers;

using Trellis.Primitives;

/// <summary>
/// Test entity representing a customer with various Trellis value object properties.
/// Uses partial properties with the <c>MaybePartialPropertyGenerator</c> source generator.
/// </summary>
public partial class TestCustomer
{
    public TestCustomerId Id { get; set; } = null!;
    public TestCustomerName Name { get; set; } = null!;
    public EmailAddress Email { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public partial Maybe<PhoneNumber> Phone { get; set; }

    public List<TestOrder> Orders { get; set; } = [];
}

public class DerivedTestCustomer : TestCustomer
{
}

/// <summary>
/// Test entity representing an order with foreign key to customer.
/// Uses partial properties with the <c>MaybePartialPropertyGenerator</c> source generator.
/// </summary>
public partial class TestOrder
{
    public TestOrderId Id { get; set; } = null!;
    public TestCustomerId CustomerId { get; set; } = null!;
    public TestCustomer Customer { get; set; } = null!;
    public decimal Amount { get; set; }
    public TestOrderStatus Status { get; set; } = null!;

    public partial Maybe<TestTicketNumber> TicketNumber { get; set; }
    public partial Maybe<TestUnitPrice> UnitPrice { get; set; }

    public partial Maybe<TestOrderStatus> OptionalStatus { get; set; }

    public partial Maybe<DateTime> SubmittedAt { get; set; }
}