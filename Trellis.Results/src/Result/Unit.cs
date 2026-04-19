namespace Trellis;

/// <summary>
/// Represents the absence of a meaningful value, used for operations that have no return value.
/// This is the functional programming equivalent of <c>void</c>, but as a proper type that can be used with generics.
/// </summary>
/// <remarks>
/// <para>
/// In functional programming, every function returns a value. When an operation doesn't have a meaningful
/// return value (like <c>void</c> methods in imperative programming), we use <c>Unit</c> as a placeholder type.
/// This allows <see cref="Result{TValue}"/> to represent both operations that return values and operations
/// that only succeed or fail without returning data.
/// </para>
/// <para>
/// Common use cases for <c>Unit</c>:
/// <list type="bullet">
/// <item>DELETE operations that either succeed or fail but don't return data</item>
/// <item>State-changing operations (e.g., sending emails, updating records)</item>
/// <item>Validation operations that only need to indicate success/failure</item>
/// <item>Fire-and-forget side effects in a functional pipeline</item>
/// </list>
/// </para>
/// <para>
/// The <see cref="Result"/> class provides convenience methods for working with <c>Unit</c>:
/// <list type="bullet">
/// <item><see cref="Result.Ok()"/> - Creates a successful <c>Result&lt;Unit&gt;</c></item>
/// <item><see cref="Result.Fail(Error)"/> - Creates a failed <c>Result&lt;Unit&gt;</c></item>
/// </list>
/// </para>
/// <para>
/// When converting <c>Result&lt;Unit&gt;</c> to HTTP responses, successful results automatically
/// map to HTTP 204 No Content, indicating success without a response body.
/// </para>
/// </remarks>
/// <example>
/// Basic usage with Result:
/// <code>
/// // Operation that doesn't return a value
/// public Result&lt;Unit&gt; DeleteUser(UserId userId)
/// {
///     var user = _repository.Find(userId);
///     if (user == null)
///         return Error.NotFound($"User {userId} not found");
///     
///     _repository.Delete(user);
///     return Result.Ok(); // Returns Result&lt;Unit&gt;
/// }
/// 
/// // Usage
/// var result = DeleteUser(userId);
/// if (result.IsSuccess)
///     Console.WriteLine("User deleted successfully");
/// </code>
/// </example>
/// <example>
/// Chaining operations that don't return values:
/// <code>
/// public async Task&lt;Result&lt;Unit&gt;&gt; ProcessOrderAsync(Order order, CancellationToken ct)
/// {
///     return await ValidateOrder(order)          // Result&lt;Unit&gt;
///         .BindAsync(() => ChargePaymentAsync(order, ct))   // Result&lt;Unit&gt;
///         .TapAsync(() => SendConfirmationEmailAsync(order.Email, ct))
///         .TapAsync(() => UpdateInventoryAsync(order.Items, ct));
/// }
/// </code>
/// </example>
/// <example>
/// Converting to HTTP response:
/// <code>
/// app.MapDelete("/users/{id}", (Guid id, IUserService userService) =>
///     UserId.TryCreate(id)
///         .Bind(userService.DeleteUser)
///         .ToHttpResult());
/// // Returns 204 No Content on success, appropriate error status on failure
/// </code>
/// </example>
/// <example>
/// Combining Unit results with value results:
/// <code>
/// public Result&lt;User&gt; CreateAndNotify(CreateUserRequest request)
/// {
///     return FirstName.TryCreate(request.FirstName)
///         .Combine(LastName.TryCreate(request.LastName))
///         .Bind((first, last) => User.Create(first, last))
///         .Combine(ValidateBusinessRules(request))  // Result&lt;Unit&gt; - just validates
///         .Map((user, _) => user);  // Discard Unit, keep User
/// }
/// </code>
/// </example>
/// <seealso cref="Result"/>
/// <seealso cref="Result{TValue}"/>
public record struct Unit
{
}