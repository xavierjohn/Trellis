namespace TestingPatterns;

using System.Diagnostics;
using Trellis;
using Xunit;

public class AsyncUsageExamples : IClassFixture<TraceFixture>
{

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Promote_customer(int id)
    {
        List<Customer> loadedCustomers = [];
        List<(Customer Customer, bool WasPromoted)> notifications = [];

        using var activity = TraceFixture.ActivitySource.StartActivity();
        var result = await GetCustomerByIdAsync(id)
            .EnsureNotNullAsync(() => new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Customer with such Id is not found: " + id })
            .TapAsync(loadedCustomers.Add)
            .EnsureAsync(customer => customer.CanBePromoted, _ => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "The customer has the highest status possible" })
            .TapAsync(customer => customer.Promote())
            .BindAsync(customer =>
            {
                notifications.Add((customer, customer.Promoted));
                return EmailGateway.SendPromotionNotification(customer.Email);
            })
            .MatchAsync(_ => "Okay", error => "Failed");

        var loadedCustomer = loadedCustomers.Should().ContainSingle().Which;
        if (id == 1)
        {
            result.Should().Be("Okay");
            loadedCustomer.Promoted.Should().BeTrue();
            var notification = notifications.Should().ContainSingle().Which;
            notification.Customer.Should().BeSameAs(loadedCustomer);
            notification.WasPromoted.Should().BeTrue();
        }
        else
        {
            result.Should().Be("Failed");
            loadedCustomer.Promoted.Should().BeFalse();
            notifications.Should().BeEmpty();
        }
    }

    [Fact]
    public static async Task PromoteAsync_customer()
    {
        var id = 1;
        List<Customer> loadedCustomers = [];
        List<(Customer Customer, bool WasPromoted)> notifications = [];

        using var activity = TraceFixture.ActivitySource.StartActivity();

        var result = await GetCustomerByIdAsync(id)
            .EnsureNotNullAsync(() => new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Customer with such Id is not found: " + id })
            .TapAsync(loadedCustomers.Add)
            .EnsureAsync(static customer => customer.CanBePromoted, static _ => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "The customer has the highest status possible" })
            .TapAsync(static customer => customer.PromoteAsync())
            .BindAsync(customer =>
            {
                notifications.Add((customer, customer.Promoted));
                return EmailGateway.SendPromotionNotificationAsync(customer.Email);
            })
            .MatchAsync(static (Unit _) => "Okay", static error => error.Detail);

        result.Should().Be("Okay");
        var loadedCustomer = loadedCustomers.Should().ContainSingle().Which;
        loadedCustomer.Promoted.Should().BeTrue();
        var notification = notifications.Should().ContainSingle().Which;
        notification.Customer.Should().BeSameAs(loadedCustomer);
        notification.WasPromoted.Should().BeTrue();
    }

    [Fact]
    public static async Task Ask_manager_for_promotion()
    {
        var id = 2;
        List<long> managerRequests = [];
        List<Customer> approvedCustomers = [];
        List<(Customer Customer, bool WasPromoted)> notifications = [];

        using var activity = TraceFixture.ActivitySource.StartActivity();

        var result = await GetCustomerByIdAsync(id)
            .EnsureNotNullAsync(() => new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Customer with such Id is not found: " + id })
            .EnsureAsync(customer => customer.CanBePromoted, _ => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Need to ask manager" })
            .TapOnFailureAsync(Log)
            .RecoverOnFailureAsync(() =>
            {
                managerRequests.Add(id);
                return AskManagerAsync(id).TapAsync(approvedCustomers.Add);
            })
            .TapAsync(static customer => Log("Manager approved promotion"))
            .TapAsync(static customer => customer.PromoteAsync())
            .BindAsync(customer =>
            {
                notifications.Add((customer, customer.Promoted));
                return EmailGateway.SendPromotionNotificationAsync(customer.Email);
            })
            .MatchAsync(static (Unit _) => "Okay", static error => error.Detail);

        result.Should().Be("Okay");
        managerRequests.Should().Equal([id]);
        var approvedCustomer = approvedCustomers.Should().ContainSingle().Which;
        approvedCustomer.Promoted.Should().BeTrue();
        var notification = notifications.Should().ContainSingle().Which;
        notification.Customer.Should().BeSameAs(approvedCustomer);
        notification.WasPromoted.Should().BeTrue();
    }

    static void Log(Error error)
    {
    }

    static void Log(string message)
    {
    }

    static Task<Result<Customer>> AskManagerAsync(long id) => Result.Ok(new Customer(true)).AsTask();

    public static Task<Customer?> GetCustomerByIdAsync(long id)
    {
        var customer = id switch
        {
            1 => new Customer(true),
            2 => new Customer(false),
            _ => null
        };

        return Task.FromResult(customer);
    }

    public class Customer
    {
        public Customer(bool canBePromoted) => CanBePromoted = canBePromoted;

        public string Email { get; } = "random@universe.com";

        public bool Promoted { get; set; }

        public bool CanBePromoted { get; }

        public void Promote() => Promoted = true;

        public Task PromoteAsync()
        {
            Promoted = true;
            return Task.CompletedTask;
        }
    }

    public static class EmailGateway
    {
        public static Result<Unit> SendPromotionNotification(string email) => Result.Ok();

        public static Task<Result<Unit>> SendPromotionNotificationAsync(string email) => SendPromotionNotification(email).AsTask();
    }
}