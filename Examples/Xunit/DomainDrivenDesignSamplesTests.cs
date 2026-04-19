using Trellis.Testing;
namespace Example.Tests;

using Trellis;
using Xunit;

/// <summary>
/// Comprehensive tests validating all examples from DomainDrivenDesign\SAMPLES.md
/// These tests prove that the sample code patterns work correctly.
/// </summary>
public class DomainDrivenDesignSamplesTests
{
    #region Test Data and Mock Domain Objects

    // Entity IDs
    public class CustomerId : ScalarValueObject<CustomerId, Guid>, IScalarValue<CustomerId, Guid>
    {
        private CustomerId(Guid value) : base(value) { }

        public static CustomerId NewUnique() => new(Guid.NewGuid());

        public static Result<CustomerId> TryCreate(Guid value, string? fieldName = null) =>
            value == Guid.Empty
                ? Result.Fail<CustomerId>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "customerId"), "validation.error") { Detail = "Customer ID cannot be empty" })))
                : Result.Ok(new CustomerId(value));

        public static Result<CustomerId> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();

        public static Result<CustomerId> TryCreate(Guid? value) =>
            value.ToResult(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Customer ID cannot be empty" })
                .Ensure(v => v != Guid.Empty, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Customer ID cannot be empty" })
                .Map(v => new CustomerId(v));
    }

    public class OrderId : ScalarValueObject<OrderId, Guid>, IScalarValue<OrderId, Guid>
    {
        private OrderId(Guid value) : base(value) { }

        public static OrderId NewUnique() => new(Guid.NewGuid());

        public static Result<OrderId> TryCreate(Guid value, string? fieldName = null) =>
            value == Guid.Empty
                ? Result.Fail<OrderId>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "orderId"), "validation.error") { Detail = "Order ID cannot be empty" })))
                : Result.Ok(new OrderId(value));

        public static Result<OrderId> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();

        public static Result<OrderId> TryCreate(Guid? value) =>
            value.ToResult(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Order ID cannot be empty" })
                .Ensure(v => v != Guid.Empty, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Order ID cannot be empty" })
                .Map(v => new OrderId(v));
    }

    public class ProductId : ScalarValueObject<ProductId, string>, IScalarValue<ProductId, string>
    {
        private ProductId(string value) : base(value) { }

        public static Result<ProductId> TryCreate(string? value, string? fieldName = null) =>
            value.ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "productId"), "validation.error") { Detail = "Product ID cannot be empty" })))
                .Ensure(v => !string.IsNullOrWhiteSpace(v), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "productId"), "validation.error") { Detail = "Product ID cannot be empty" })))
                .Map(v => new ProductId(v));
    }

    // Simple value object for testing
    public class EmailAddress : ScalarValueObject<EmailAddress, string>, IScalarValue<EmailAddress, string>
    {
        private EmailAddress(string value) : base(value) { }

        public static Result<EmailAddress> TryCreate(string? value, string? fieldName = null) =>
            value.ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "email"), "validation.error") { Detail = "Email cannot be empty" })))
                .Ensure(v => !string.IsNullOrWhiteSpace(v), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "email"), "validation.error") { Detail = "Email cannot be empty" })))
                .Ensure(v => v.Contains('@'), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "email"), "validation.error") { Detail = "Email must contain @" })))
                .Map(v => new EmailAddress(v));
    }

    #endregion

    #region Entity Examples Tests

    public class Customer : Entity<CustomerId>
    {
        public string Name { get; private set; }
        public EmailAddress Email { get; private set; }

        private Customer(CustomerId id, string name, EmailAddress email)
            : base(id)
        {
            Name = name;
            Email = email;
        }

        public static Result<Customer> TryCreate(string name, EmailAddress email) =>
            name.ToResult()
                .Ensure(n => !string.IsNullOrWhiteSpace(n),
                       new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Name cannot be empty" })
                .Map(n => new Customer(CustomerId.NewUnique(), n, email));

        public static Customer Create(string name, EmailAddress email)
        {
            var result = TryCreate(name, email);
            if (result.IsFailure)
                throw new InvalidOperationException($"Failed to create Customer: {result.UnwrapError().Detail}");

            return result.Unwrap();
        }

        public Result<Customer> UpdateName(string newName) =>
            newName.ToResult()
                .Ensure(n => !string.IsNullOrWhiteSpace(n),
                       new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Name cannot be empty" })
                .Tap(n => Name = n)
                .Map(_ => this);

        public Result<Customer> UpdateEmail(EmailAddress newEmail) =>
            newEmail.ToResult()
                .Tap(e => Email = e)
                .Map(_ => this);
    }

    [Fact]
    public void EntityExample_CreateCustomer_Succeeds()
    {
        // Arrange
        var email = EmailAddress.Create("john@example.com");

        // Act
        var result = Customer.TryCreate("John Doe", email);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Name.Should().Be("John Doe");
        result.Unwrap().Email.Should().Be(email);
    }

    [Fact]
    public void EntityExample_CreateCustomerWithEmptyName_Fails()
    {
        // Arrange
        var email = EmailAddress.Create("john@example.com");

        // Act
        var result = Customer.TryCreate("", email);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().GetDisplayMessage().Should().Contain("Name cannot be empty");
    }

    [Fact]
    public void EntityExample_TwoCustomersWithSameData_HaveDifferentIdentity()
    {
        // Arrange
        var email = EmailAddress.Create("john@example.com");

        // Act
        var customer1 = Customer.TryCreate("John Doe", email);
        var customer2 = Customer.TryCreate("John Doe", email);

        // Assert - Different instances, different IDs, not equal
        customer1.Unwrap().Should().NotBe(customer2.Unwrap());
        customer1.Unwrap().Id.Should().NotBe(customer2.Unwrap().Id);
    }

    [Fact]
    public void EntityExample_UpdateName_Succeeds()
    {
        // Arrange
        var email = EmailAddress.Create("john@example.com");
        var customer = Customer.Create("John Doe", email);

        // Act
        var result = customer.UpdateName("John Smith");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Name.Should().Be("John Smith");
    }

    [Fact]
    public void EntityExample_UpdateEmail_Succeeds()
    {
        // Arrange
        var email = EmailAddress.Create("john@example.com");
        var customer = Customer.Create("John Doe", email);
        var newEmail = EmailAddress.Create("john.smith@example.com");

        // Act
        var result = customer.UpdateEmail(newEmail);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Email.Should().Be(newEmail);
    }

    [Fact]
    public void EntityExample_ChainedUpdates_Succeeds()
    {
        // Arrange
        var email = EmailAddress.Create("john@example.com");
        var customer = Customer.Create("John Doe", email);
        var newEmail = EmailAddress.Create("john.smith@example.com");

        // Act
        var result = customer
            .UpdateName("John Smith")
            .Bind(c => c.UpdateEmail(newEmail));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Name.Should().Be("John Smith");
        result.Unwrap().Email.Should().Be(newEmail);
    }

    #endregion

    #region Value Object Examples Tests

    // Address (Multi-Property)
    public class Address : ValueObject
    {
        public string Street { get; }
        public string City { get; }
        public string State { get; }
        public string PostalCode { get; }
        public string Country { get; }

        private Address(string street, string city, string state, string postalCode, string country)
        {
            Street = street;
            City = city;
            State = state;
            PostalCode = postalCode;
            Country = country;
        }

        public static Result<Address> TryCreate(
            string street,
            string city,
            string state,
            string postalCode,
            string country) =>
            (street, city, state, postalCode, country).ToResult()
                .Ensure(x => !string.IsNullOrWhiteSpace(x.street),
                       new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(street)), "validation.error") { Detail = "Street is required" })))
                .Ensure(x => !string.IsNullOrWhiteSpace(x.city),
                       new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(city)), "validation.error") { Detail = "City is required" })))
                .Ensure(x => !string.IsNullOrWhiteSpace(x.state),
                       new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(state)), "validation.error") { Detail = "State is required" })))
                .Ensure(x => !string.IsNullOrWhiteSpace(x.postalCode),
                       new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(postalCode)), "validation.error") { Detail = "Postal code is required" })))
                .Ensure(x => !string.IsNullOrWhiteSpace(x.country),
                       new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(country)), "validation.error") { Detail = "Country is required" })))
                .Map(x => new Address(x.street, x.city, x.state, x.postalCode, x.country));

        protected override IEnumerable<IComparable?> GetEqualityComponents()
        {
            yield return Street;
            yield return City;
            yield return State;
            yield return PostalCode;
            yield return Country;
        }

        public string GetFullAddress() => $"{Street}, {City}, {State} {PostalCode}, {Country}";

        public bool IsSameCity(Address other) =>
            City.Equals(other.City, StringComparison.OrdinalIgnoreCase) &&
            State.Equals(other.State, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValueObject_AddressWithSameValues_AreEqual()
    {
        // Arrange & Act
        var address1 = Address.TryCreate("123 Main St", "Springfield", "IL", "62701", "USA");
        var address2 = Address.TryCreate("123 Main St", "Springfield", "IL", "62701", "USA");

        // Assert - Same values, equal
        address1.Unwrap().Should().Be(address2.Unwrap());
    }

    [Fact]
    public void ValueObject_AddressWithDifferentValues_AreNotEqual()
    {
        // Arrange & Act
        var address1 = Address.TryCreate("123 Main St", "Springfield", "IL", "62701", "USA");
        var address3 = Address.TryCreate("456 Oak Ave", "Springfield", "IL", "62702", "USA");

        // Assert - Different values, not equal
        address1.Unwrap().Should().NotBe(address3.Unwrap());
    }

    [Fact]
    public void ValueObject_AddressInSameCity_ReturnsTrue()
    {
        // Arrange
        var address1 = Address.TryCreate("123 Main St", "Springfield", "IL", "62701", "USA");
        var address3 = Address.TryCreate("456 Oak Ave", "Springfield", "IL", "62702", "USA");

        // Act & Assert
        address1.Unwrap().IsSameCity(address3.Unwrap()).Should().BeTrue();
    }

    [Fact]
    public void ValueObject_AddressGetFullAddress_ReturnsFormattedString()
    {
        // Arrange
        var address = Address.TryCreate("123 Main St", "Springfield", "IL", "62701", "USA");

        // Act & Assert
        address.Unwrap().GetFullAddress().Should().Be("123 Main St, Springfield, IL 62701, USA");
    }

    // Temperature (Scalar)
    public class Temperature : ScalarValueObject<Temperature, decimal>, IScalarValue<Temperature, decimal>
    {
        private Temperature(decimal value) : base(value) { }

        public static Result<Temperature> TryCreate(decimal value, string? fieldName = null)
        {
            var field = fieldName ?? "temperature";
            return value.ToResult()
                .Ensure(v => v >= -273.15m,
                       new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Temperature cannot be below absolute zero" })))
                .Ensure(v => v <= 1_000_000m,
                       new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Temperature exceeds physical limits" })))
                .Map(v => new Temperature(v));
        }

        public static Result<Temperature> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();

        public static Temperature FromCelsius(decimal celsius) => new(celsius);
        public static Temperature FromFahrenheit(decimal fahrenheit) => new((fahrenheit - 32) * 5 / 9);
        public static Temperature FromKelvin(decimal kelvin) => new(kelvin - 273.15m);

        protected override IEnumerable<IComparable?> GetEqualityComponents()
        {
            yield return Math.Round(Value, 2);
        }

        public Temperature Add(Temperature other) => new(Value + other.Value);
        public Temperature Subtract(Temperature other) => new(Value - other.Value);

        public decimal ToCelsius() => Value;
        public decimal ToFahrenheit() => (Value * 9 / 5) + 32;
        public decimal ToKelvin() => Value + 273.15m;

        public bool IsAboveZero => Value > 0;
        public bool IsBelowZero => Value < 0;
        public bool IsFreezing => Value <= 0;
        public bool IsBoiling => Value >= 100;
    }

    [Fact]
    public void ValueObject_Temperature_CreateValid_Succeeds()
    {
        // Act
        var result = Temperature.TryCreate(98.6m);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(98.6m);
    }

    [Fact]
    public void ValueObject_Temperature_BelowAbsoluteZero_Fails()
    {
        // Act
        var result = Temperature.TryCreate(-300m);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().GetDisplayMessage().Should().Contain("below absolute zero");
    }

    [Fact]
    public void ValueObject_Temperature_RoundedEqual()
    {
        // Arrange & Act
        var temp1 = Temperature.TryCreate(98.6m);
        var temp2 = Temperature.TryCreate(98.60m);

        // Assert - Rounded to same value, equal
        temp1.Unwrap().Should().Be(temp2.Unwrap());
    }

    [Fact]
    public void ValueObject_Temperature_ConversionFromFahrenheit()
    {
        // Act
        var tempF = Temperature.FromFahrenheit(98.6m);

        // Assert
        tempF.Value.Should().BeApproximately(37m, 0.1m);
    }

    [Fact]
    public void ValueObject_Temperature_DomainOperations()
    {
        // Arrange
        var temp1 = Temperature.TryCreate(100m);
        var temp2 = Temperature.TryCreate(50m);

        // Act
        var difference = temp1.Unwrap().Subtract(temp2.Unwrap());

        // Assert
        difference.Value.Should().Be(50m);
    }

    [Fact]
    public void ValueObject_Temperature_BooleanProperties()
    {
        // Arrange
        var hotTemp = Temperature.TryCreate(150m);
        var coldTemp = Temperature.TryCreate(-10m);

        // Assert
        hotTemp.Unwrap().IsBoiling.Should().BeTrue();
        coldTemp.Unwrap().IsFreezing.Should().BeTrue();
        coldTemp.Unwrap().IsBelowZero.Should().BeTrue();
    }

    // Money (Domain Logic)
    public class Money : ValueObject
    {
        public decimal Amount { get; }
        public string Currency { get; }

        private Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        public static Result<Money> TryCreate(decimal amount, string currency = "USD") =>
            (amount, currency).ToResult()
                .Ensure(x => x.amount >= 0,
                       new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(amount)), "validation.error") { Detail = "Amount cannot be negative" })))
                .Ensure(x => !string.IsNullOrWhiteSpace(x.currency),
                       new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(currency)), "validation.error") { Detail = "Currency is required" })))
                .Ensure(x => x.currency.Length == 3,
                       new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(currency)), "validation.error") { Detail = "Currency must be 3-letter ISO code" })))
                .Map(x => new Money(x.amount, x.currency.ToUpperInvariant()));

        public static Money Create(decimal amount, string currency = "USD")
        {
            var result = TryCreate(amount, currency);
            if (result.IsFailure)
                throw new InvalidOperationException($"Failed to create Money: {result.UnwrapError().Detail}");

            return result.Unwrap();
        }

        public static Money Zero(string currency = "USD") => new(0, currency);

        protected override IEnumerable<IComparable?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }

        public Result<Money> Add(Money other) =>
            Currency != other.Currency
                ? Result.Fail<Money>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Cannot add {other.Currency} to {Currency}" })
                : new Money(Amount + other.Amount, Currency).ToResult();

        public Result<Money> Subtract(Money other) =>
            Currency != other.Currency
                ? Result.Fail<Money>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Cannot subtract {other.Currency} from {Currency}" })
                : Amount < other.Amount
                    ? Result.Fail<Money>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Result would be negative" })
                    : new Money(Amount - other.Amount, Currency).ToResult();

        public Money Multiply(decimal factor) =>
            factor < 0
                ? throw new ArgumentException("Factor cannot be negative", nameof(factor))
                : new Money(Amount * factor, Currency);

        public Money Divide(decimal divisor) =>
            divisor <= 0
                ? throw new ArgumentException("Divisor must be positive", nameof(divisor))
                : new Money(Amount / divisor, Currency);

        public Money ApplyDiscount(decimal percentage) =>
            percentage is < 0 or > 100
                ? throw new ArgumentException("Percentage must be between 0 and 100", nameof(percentage))
                : new Money(Amount * (1 - (percentage / 100m)), Currency);

        public bool IsZero => Amount == 0;
        public bool IsPositive => Amount > 0;
    }

    [Fact]
    public void ValueObject_Money_CreateValid_Succeeds()
    {
        // Act
        var result = Money.TryCreate(100.00m, "USD");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(100.00m);
        result.Unwrap().Currency.Should().Be("USD");
    }

    [Fact]
    public void ValueObject_Money_NegativeAmount_Fails()
    {
        // Act
        var result = Money.TryCreate(-10.00m, "USD");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().GetDisplayMessage().Should().Contain("Amount cannot be negative");
    }

    [Fact]
    public void ValueObject_Money_AddSameCurrency_Succeeds()
    {
        // Arrange
        var money1 = Money.TryCreate(100.00m, "USD");
        var money2 = Money.TryCreate(50.00m, "USD");

        // Act
        var result = money1.Unwrap().Add(money2.Unwrap());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(150.00m);
    }

    [Fact]
    public void ValueObject_Money_AddDifferentCurrency_Fails()
    {
        // Arrange
        var usd = Money.TryCreate(100.00m, "USD");
        var eur = Money.TryCreate(50.00m, "EUR");

        // Act
        var result = usd.Unwrap().Add(eur.Unwrap());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().GetDisplayMessage().Should().Contain("Cannot add EUR to USD");
    }

    [Fact]
    public void ValueObject_Money_ApplyDiscount_Succeeds()
    {
        // Arrange
        var price = Money.TryCreate(100.00m, "USD");

        // Act
        var discount = price.Unwrap().ApplyDiscount(10);

        // Assert
        discount.Amount.Should().Be(90.00m);
    }

    [Fact]
    public void ValueObject_Money_MultiplyAndAdd_ChainOperations()
    {
        // Arrange
        var price = Money.TryCreate(100.00m, "USD");

        // Act
        var discount = price.Unwrap().ApplyDiscount(10); // $90.00
        var tax = discount.Multiply(0.08m); // $7.20
        var total = discount.Add(tax); // $97.20

        // Assert
        total.IsSuccess.Should().BeTrue();
        total.Unwrap().Amount.Should().Be(97.20m);
    }

    #endregion

    #region Aggregate Examples Tests

    // Domain Events
    public record OrderCreated(OrderId OrderId, CustomerId CustomerId, DateTime OccurredAt) : IDomainEvent;
    public record OrderLineAdded(OrderId OrderId, ProductId ProductId, int Quantity, DateTime OccurredAt) : IDomainEvent;
    public record OrderLineRemoved(OrderId OrderId, ProductId ProductId, DateTime OccurredAt) : IDomainEvent;
    public record OrderSubmitted(OrderId OrderId, Money Total, DateTime OccurredAt) : IDomainEvent;
    public record OrderCancelled(OrderId OrderId, string Reason, DateTime OccurredAt) : IDomainEvent;
    public record OrderShipped(OrderId OrderId, DateTime OccurredAt) : IDomainEvent;

    public enum OrderStatus
    {
        Draft,
        Submitted,
        Processing,
        Shipped,
        Delivered,
        Cancelled
    }

    // Order Line Entity
    public class OrderLine : Entity<Guid>
    {
        public ProductId ProductId { get; }
        public string ProductName { get; }
        public Money Price { get; }
        public int Quantity { get; private set; }
        public Money LineTotal => Price.Multiply(Quantity);

        public OrderLine(ProductId productId, string productName, Money price, int quantity)
            : base(Guid.NewGuid())
        {
            ProductId = productId;
            ProductName = productName;
            Price = price;
            Quantity = quantity;
        }

        public void UpdateQuantity(int newQuantity) =>
            Quantity = newQuantity > 0
                ? newQuantity
                : throw new ArgumentException("Quantity must be positive", nameof(newQuantity));
    }

    // Aggregate Root
    public class Order : Aggregate<OrderId>
    {
        private readonly List<OrderLine> _lines = [];

        public CustomerId CustomerId { get; }
        public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
        public Money Total { get; private set; }
        public OrderStatus Status { get; private set; }
        public DateTime? SubmittedAt { get; private set; }
        public DateTime? ShippedAt { get; private set; }
        public DateTime? CancelledAt { get; private set; }

        private Order(OrderId id, CustomerId customerId) : base(id)
        {
            CustomerId = customerId;
            Status = OrderStatus.Draft;
            Total = Money.Create(0m, "USD");

            DomainEvents.Add(new OrderCreated(id, customerId, DateTime.UtcNow));
        }

        public static Result<Order> TryCreate(CustomerId customerId) =>
            customerId.ToResult()
                .Map(cid => new Order(OrderId.NewUnique(), cid));

        public static Order Create(CustomerId customerId)
        {
            var result = TryCreate(customerId);
            if (result.IsFailure)
                throw new InvalidOperationException($"Failed to create Order: {result.UnwrapError().Detail}");

            return result.Unwrap();
        }

        public Result<Order> AddLine(ProductId productId, string productName, Money price, int quantity) =>
            this.ToResult()
                .Ensure(_ => Status == OrderStatus.Draft,
                       new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Can only add items to draft orders" })
                .Ensure(_ => quantity > 0,
                       new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(quantity)), "validation.error") { Detail = "Quantity must be positive" })))
                .Ensure(_ => quantity <= 1000,
                       new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(quantity)), "validation.error") { Detail = "Quantity cannot exceed 1000" })))
                .Tap(_ =>
                {
                    var existingLine = _lines.FirstOrDefault(l => l.ProductId == productId);
                    if (existingLine != null)
                    {
                        existingLine.UpdateQuantity(existingLine.Quantity + quantity);
                    }
                    else
                    {
                        var line = new OrderLine(productId, productName, price, quantity);
                        _lines.Add(line);
                    }

                    RecalculateTotal();
                    DomainEvents.Add(new OrderLineAdded(Id, productId, quantity, DateTime.UtcNow));
                });

        public Result<Order> RemoveLine(ProductId productId) =>
            this.ToResult()
                .Ensure(_ => Status == OrderStatus.Draft,
                       new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Can only remove items from draft orders" })
                .Ensure(_ => _lines.Any(l => l.ProductId == productId),
                       new Error.NotFound(new ResourceRef("Resource", null)) { Detail = $"Product {productId} not found in order" })
                .Tap(_ =>
                {
                    var line = _lines.First(l => l.ProductId == productId);
                    _lines.Remove(line);
                    RecalculateTotal();
                    DomainEvents.Add(new OrderLineRemoved(Id, productId, DateTime.UtcNow));
                });

        public Result<Order> Submit() =>
            this.ToResult()
                .Ensure(_ => Status == OrderStatus.Draft,
                       new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Can only submit draft orders" })
                .Ensure(_ => Lines.Count > 0,
                       new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Cannot submit empty order" })
                .Ensure(_ => Total.Amount > 0,
                       new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Order total must be positive" })
                .Tap(_ =>
                {
                    Status = OrderStatus.Submitted;
                    SubmittedAt = DateTime.UtcNow;
                    DomainEvents.Add(new OrderSubmitted(Id, Total, DateTime.UtcNow));
                });

        public Result<Order> Ship() =>
            this.ToResult()
                .Ensure(_ => Status == OrderStatus.Submitted,
                       new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Can only ship submitted orders" })
                .Tap(_ =>
                {
                    Status = OrderStatus.Shipped;
                    ShippedAt = DateTime.UtcNow;
                    DomainEvents.Add(new OrderShipped(Id, DateTime.UtcNow));
                });

        public Result<Order> Cancel(string reason) =>
            this.ToResult()
                .Ensure(_ => Status is OrderStatus.Draft or OrderStatus.Submitted,
                       new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Can only cancel draft or submitted orders" })
                .Ensure(_ => !string.IsNullOrWhiteSpace(reason),
                       new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(reason)), "validation.error") { Detail = "Cancellation reason is required" })))
                .Tap(_ =>
                {
                    Status = OrderStatus.Cancelled;
                    CancelledAt = DateTime.UtcNow;
                    DomainEvents.Add(new OrderCancelled(Id, reason, DateTime.UtcNow));
                });

        private void RecalculateTotal()
        {
            var total = 0m;
            for (int i = 0; i < Lines.Count; i++)
            {
                total += Lines[i].Price.Amount * Lines[i].Quantity;
            }

            Total = Money.Create(total, Lines.Count > 0 ? Lines[0].Price.Currency : "USD");
        }
    }

    [Fact]
    public void Aggregate_CreateOrder_Succeeds()
    {
        // Arrange
        var customerId = CustomerId.NewUnique();

        // Act
        var result = Order.TryCreate(customerId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().CustomerId.Should().Be(customerId);
        result.Unwrap().Status.Should().Be(OrderStatus.Draft);
        result.Unwrap().Lines.Should().BeEmpty();
        result.Unwrap().Total.Amount.Should().Be(0);
    }

    [Fact]
    public void Aggregate_CreateOrder_GeneratesOrderCreatedEvent()
    {
        // Arrange
        var customerId = CustomerId.NewUnique();

        // Act
        var order = Order.Create(customerId);

        // Assert
        order.UncommittedEvents().Count.Should().Be(1);
        order.UncommittedEvents()[0].Should().BeOfType<OrderCreated>();
    }

    [Fact]
    public void Aggregate_AddLine_Succeeds()
    {
        // Arrange
        var customerId = CustomerId.NewUnique();
        var order = Order.Create(customerId);
        var productId = ProductId.Create("PROD-001");
        var price = Money.Create(29.99m, "USD");

        // Act
        var result = order.AddLine(productId, "Widget", price, 5);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Lines.Should().HaveCount(1);
        result.Unwrap().Lines[0].ProductName.Should().Be("Widget");
        result.Unwrap().Lines[0].Quantity.Should().Be(5);
        result.Unwrap().Total.Amount.Should().Be(149.95m);
    }

    [Fact]
    public void Aggregate_AddLineTwice_IncreasesQuantity()
    {
        // Arrange
        var customerId = CustomerId.NewUnique();
        var order = Order.Create(customerId);
        var productId = ProductId.Create("PROD-001");
        var price = Money.Create(29.99m, "USD");

        // Act
        var result = order
            .AddLine(productId, "Widget", price, 5)
            .Bind(o => o.AddLine(productId, "Widget", price, 3));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Lines.Should().HaveCount(1);
        result.Unwrap().Lines[0].Quantity.Should().Be(8);
    }

    [Fact]
    public void Aggregate_RemoveLine_Succeeds()
    {
        // Arrange
        var customerId = CustomerId.NewUnique();
        var order = Order.Create(customerId);
        var productId = ProductId.Create("PROD-001");
        var price = Money.Create(29.99m, "USD");
        order.AddLine(productId, "Widget", price, 5);

        // Act
        var result = order.RemoveLine(productId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Lines.Should().BeEmpty();
        result.Unwrap().Total.Amount.Should().Be(0);
    }

    [Fact]
    public void Aggregate_SubmitOrder_Succeeds()
    {
        // Arrange
        var customerId = CustomerId.NewUnique();
        var order = Order.Create(customerId);
        var productId = ProductId.Create("PROD-001");
        var price = Money.Create(29.99m, "USD");
        order.AddLine(productId, "Widget", price, 5);

        // Act
        var result = order.Submit();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Status.Should().Be(OrderStatus.Submitted);
        result.Unwrap().SubmittedAt.Should().NotBeNull();
    }

    [Fact]
    public void Aggregate_SubmitEmptyOrder_Fails()
    {
        // Arrange
        var customerId = CustomerId.NewUnique();
        var order = Order.Create(customerId);

        // Act
        var result = order.Submit();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().GetDisplayMessage().Should().Contain("Cannot submit empty order");
    }

    [Fact]
    public void Aggregate_ShipOrder_Succeeds()
    {
        // Arrange
        var customerId = CustomerId.NewUnique();
        var order = Order.Create(customerId);
        var productId = ProductId.Create("PROD-001");
        var price = Money.Create(29.99m, "USD");
        order.AddLine(productId, "Widget", price, 5);
        order.Submit();

        // Act
        var result = order.Ship();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Status.Should().Be(OrderStatus.Shipped);
        result.Unwrap().ShippedAt.Should().NotBeNull();
    }

    [Fact]
    public void Aggregate_CancelOrder_Succeeds()
    {
        // Arrange
        var customerId = CustomerId.NewUnique();
        var order = Order.Create(customerId);
        var productId = ProductId.Create("PROD-001");
        var price = Money.Create(29.99m, "USD");
        order.AddLine(productId, "Widget", price, 5);

        // Act
        var result = order.Cancel("Customer requested cancellation");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Status.Should().Be(OrderStatus.Cancelled);
        result.Unwrap().CancelledAt.Should().NotBeNull();
    }

    [Fact]
    public void Aggregate_DomainEventsTracking_Succeeds()
    {
        // Arrange
        var customerId = CustomerId.NewUnique();
        var order = Order.Create(customerId);
        var productId = ProductId.Create("PROD-001");
        var price = Money.Create(29.99m, "USD");

        // Act
        order.AddLine(productId, "Widget", price, 5);
        order.Submit();

        // Assert
        order.UncommittedEvents().Should().HaveCount(3);
        order.UncommittedEvents()[0].Should().BeOfType<OrderCreated>();
        order.UncommittedEvents()[1].Should().BeOfType<OrderLineAdded>();
        order.UncommittedEvents()[2].Should().BeOfType<OrderSubmitted>();
    }

    [Fact]
    public void Aggregate_AcceptChanges_ClearsUncommittedEvents()
    {
        // Arrange
        var customerId = CustomerId.NewUnique();
        var order = Order.Create(customerId);

        // Act
        order.AcceptChanges();

        // Assert
        order.UncommittedEvents().Should().BeEmpty();
        order.IsChanged.Should().BeFalse();
    }

    [Fact]
    public void Aggregate_CompleteOrderWorkflow_Succeeds()
    {
        // Arrange
        var customerId = CustomerId.NewUnique();
        var productId1 = ProductId.Create("PROD-001");
        var productId2 = ProductId.Create("PROD-002");
        var price1 = Money.Create(29.99m, "USD");
        var price2 = Money.Create(49.99m, "USD");

        // Act
        var result = Order.TryCreate(customerId)
            .Bind(o => o.AddLine(productId1, "Widget", price1, 5))
            .Bind(o => o.AddLine(productId2, "Gadget", price2, 3))
            .Bind(o => o.Submit())
            .Bind(o => o.Ship());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Status.Should().Be(OrderStatus.Shipped);
        result.Unwrap().Lines.Should().HaveCount(2);
        result.Unwrap().Total.Amount.Should().Be(299.92m); // (29.99*5) + (49.99*3)
    }

    #endregion
}
