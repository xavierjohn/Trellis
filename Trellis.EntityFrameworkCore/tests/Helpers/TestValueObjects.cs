namespace Trellis.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Test value object: strongly-typed customer identifier.
/// </summary>
public partial class TestCustomerId : RequiredGuid<TestCustomerId>;

/// <summary>
/// Test value object: strongly-typed order identifier.
/// </summary>
public partial class TestOrderId : RequiredGuid<TestOrderId>;

/// <summary>
/// Test value object: strongly-typed customer name.
/// </summary>
public partial class TestCustomerName : RequiredString<TestCustomerName>;

/// <summary>
/// Test value object: strongly-typed ticket number.
/// </summary>
public partial class TestTicketNumber : RequiredInt<TestTicketNumber>;

/// <summary>
/// Test value object: strongly-typed unit price.
/// </summary>
public partial class TestUnitPrice : RequiredDecimal<TestUnitPrice>;

/// <summary>
/// Test value object: strongly-typed order status enum.
/// </summary>
public partial class TestOrderStatus : RequiredEnum<TestOrderStatus>
{
    public static readonly TestOrderStatus Draft = new();
    public static readonly TestOrderStatus Confirmed = new();
    public static readonly TestOrderStatus Shipped = new();
    public static readonly TestOrderStatus Cancelled = new();
}

/// <summary>
/// Test value object: strongly-typed boolean flag.
/// </summary>
public partial class TestGiftWrap : RequiredBool<TestGiftWrap>;

/// <summary>
/// Test value object: strongly-typed date.
/// </summary>
public partial class TestOrderDate : RequiredDateTime<TestOrderDate>;