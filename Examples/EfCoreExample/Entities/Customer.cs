using Trellis.Primitives;

namespace EfCoreExample.Entities;

using EfCoreExample.ValueObjects;
using Trellis;

/// <summary>
/// Customer entity with strongly-typed ID (GUID) and validated email address.
/// </summary>
public class Customer : Entity<CustomerId>
{
    public CustomerName Name { get; private set; } = null!;
    public EmailAddress Email { get; private set; } = null!;

    // EF Core requires parameterless constructor
    private Customer() : base(CustomerId.NewUniqueV4()) { }

    private Customer(CustomerId id, CustomerName name, EmailAddress email) : base(id)
    {
        Name = name;
        Email = email;
    }

    /// <summary>
    /// Creates a new customer using Railway Oriented Programming pattern.
    /// All validation happens through the value object factory methods.
    /// </summary>
    public static Result<Customer> TryCreate(string? name, string? email) =>
        CustomerName.TryCreate(name, nameof(name))
            .Combine(EmailAddress.TryCreate(email, nameof(email)))
            .Map((customerName, emailAddress) => new Customer(
                CustomerId.NewUniqueV4(),
                customerName,
                emailAddress));

    /// <summary>
    /// Updates the customer's email address.
    /// </summary>
    public Result<Customer> UpdateEmail(string? newEmail) =>
        EmailAddress.TryCreate(newEmail, nameof(newEmail))
            .Tap(email => Email = email)
            .Map(_ => this);
}