# TRLS008: Result is double-wrapped

## Cause

A `Result<Result<T>>` type is detected, indicating that a `Result` is wrapped inside another `Result`.

## Rule Description

`Result<Result<T>>` is almost always unintended and indicates a logic error. This typically happens when:
- Using `Map` instead of `Bind` when the transformation returns a `Result`
- Manually wrapping a `Result` in another `Result`

## How to Fix Violations

### Use Bind instead of Map

```csharp
// ❌ Bad - Creates Result<Result<Customer>>
return emailResult.Map(email => Customer.Create(email));
//                                      ^^^^^^^^^^^^^^^^^^^
//                             Returns Result<Customer>

// ✅ Good - Flattens to Result<Customer>
return emailResult.Bind(email => Customer.Create(email));
```

### Don't wrap existing Results

```csharp
// ❌ Bad - Double wrapping
Result<Customer> customerResult = GetCustomer();
Result<Result<Customer>> wrapped = Result.Success(customerResult); // Wrong!

// ✅ Good - Return the Result directly
Result<Customer> customerResult = GetCustomer();
return customerResult;
```

## Example

```csharp
public Result<Order> CreateOrder(string emailStr, decimal amount)
{
    // ❌ Bad - Result<Result<Order>>
    var result = Email.TryCreate(emailStr)
        .Map(email => Order.Create(email, amount));
    //  ^^^                ^^^^^^^^^^^^^^^^^^^
    //  Map           Returns Result<Order>
    // Result is: Result<Result<Order>>

    // ✅ Good - Result<Order>
    var result = Email.TryCreate(emailStr)
        .Bind(email => Order.Create(email, amount));
    //  ^^^^
    //  Bind flattens the Result
}
```

## When to Suppress Warnings

Do not suppress this warning. `Result<Result<T>>` is never the correct type.

If you have a legitimate case (extremely rare), restructure your code to use `Bind` or unwrap one layer.

## Related Rules

- [TRLS002](TRLS002.md) - Use Bind instead of Map when lambda returns Result
- [TRLS011](TRLS011.md) - Maybe is double-wrapped
