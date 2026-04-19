using System.Globalization;
using Trellis.Asp;
using Trellis.Primitives;

namespace SampleWebApplication.Controllers;

using Microsoft.AspNetCore.Mvc;
using SampleUserLibrary;
using Trellis;

[ApiController]
[Route("[controller]")]
public class UsersController : ControllerBase
{
    [HttpPost("[action]")]
    public ActionResult<User> Register([FromBody] RegisterUserRequest request) =>
        FirstName.TryCreate(request.firstName)
        .Combine(LastName.TryCreate(request.lastName))
        .Combine(EmailAddress.TryCreate(request.email))
        .Combine(PhoneNumber.TryCreate(request.phone))
        .Combine(Age.TryCreate(request.age))
        .Combine(CountryCode.TryCreate(request.country))
        .Bind((firstName, lastName, email, phone, age, country) =>
            SampleUserLibrary.User.TryCreate(firstName, lastName, email, phone, age, country, request.password))
        .ToActionResult(this);

    [HttpPost("[action]")]
    public ActionResult<User> RegisterCreated([FromBody] RegisterUserRequest request) =>
        FirstName.TryCreate(request.firstName)
        .Combine(LastName.TryCreate(request.lastName))
        .Combine(EmailAddress.TryCreate(request.email))
        .Combine(PhoneNumber.TryCreate(request.phone))
        .Combine(Age.TryCreate(request.age))
        .Combine(CountryCode.TryCreate(request.country))
        .Bind((firstName, lastName, email, phone, age, country) =>
            SampleUserLibrary.User.TryCreate(firstName, lastName, email, phone, age, country, request.password))
        .ToCreatedAtActionResult(this,
            actionName: nameof(Get),
            routeValues: user => new { name = user.FirstName });

    [HttpPost("[action]")]
    public ActionResult<User> RegisterAccepted([FromBody] RegisterUserRequest request) =>
        FirstName.TryCreate(request.firstName)
            .Combine(LastName.TryCreate(request.lastName))
            .Combine(EmailAddress.TryCreate(request.email))
            .Combine(PhoneNumber.TryCreate(request.phone))
            .Combine(Age.TryCreate(request.age))
            .Combine(CountryCode.TryCreate(request.country))
            .Bind((firstName, lastName, email, phone, age, country) =>
                SampleUserLibrary.User.TryCreate(firstName, lastName, email, phone, age, country, request.password))
            .Match(
                onSuccess: ok => AcceptedAtAction("Get", new { name = ok.FirstName }, ok),
                onFailure: err => err.ToActionResult<User>(this));

    [HttpGet("{name}")]
    public ActionResult<string> Get(string name) => Ok($"Hello {name}!");

    [HttpGet("notfound/{id}")]
    public ActionResult NotFound(int id) =>
        Result.Fail(new Error.NotFound(new ResourceRef("Resource", id.ToString(CultureInfo.InvariantCulture))) { Detail = "User not found" })
            .ToActionResult(this);

    [HttpGet("conflict/{id}")]
    public ActionResult Conflict(int id) =>
        Result.Fail(new Error.Conflict(null, id.ToString(CultureInfo.InvariantCulture)) { Detail = "Record has changed." })
            .ToActionResult(this);

    [HttpGet("forbidden/{id}")]
    public ActionResult Forbidden(int id) =>
        Result.Fail(new Error.Forbidden(id.ToString(CultureInfo.InvariantCulture)) { Detail = "You do not have access." })
            .ToActionResult(this);

    [HttpGet("unauthorized/{id}")]
    public ActionResult Unauthorized(int id) =>
        Result.Fail(new Error.Unauthorized() { Detail = "Please log in." })
            .ToActionResult(this);

    [HttpGet("unexpected/{id}")]
    public ActionResult Unexpected(int id) =>
        Result.Fail(new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = "Something went wrong." })
            .ToActionResult(this);

    [HttpDelete("{id}")]
    public ActionResult Delete(string id) =>
        UserId.TryCreate(id).Match(
            onSuccess: ok => NoContent(),
            onFailure: err => err.ToActionResult(this));

    /// <summary>
    /// Registers a new user with automatic value object validation.
    /// No manual validation or Result.Combine() needed - the framework
    /// handles it automatically via AddScalarValueValidation().
    /// </summary>
    /// <param name="dto">Registration data with value objects.</param>
    /// <returns>
    /// 200 OK with the created user if all validations pass.
    /// 400 Bad Request with validation errors if any value object is invalid.
    /// </returns>
    /// <remarks>
    /// This endpoint demonstrates the new automatic validation feature:
    /// - 7 value objects (FirstName, LastName, EmailAddress, PhoneNumber, Age, CountryCode, Url) are validated during model binding
    /// - Invalid requests automatically return 400 with structured error messages
    /// - No manual Result.Combine() calls needed in the controller
    /// - Validation errors include field names and details
    /// </remarks>
    [HttpPost("[action]")]
    public ActionResult<User> RegisterWithAutoValidation([FromBody] RegisterUserDto dto)
    {
        // If we reach here, all value objects in dto are already validated!
        // The [ApiController] attribute automatically returns 400 if validation fails.

        Result<User> userResult = SampleUserLibrary.User.TryCreate(
            dto.FirstName,
            dto.LastName,
            dto.Email,
            dto.Phone,
            dto.Age,
            dto.Country,
            dto.Password,
            dto.Website);

        return userResult.ToActionResult(this);
    }

    /// <summary>
    /// Tests that the same value object type (Name) used for multiple properties
    /// correctly reports validation errors with the property name, not the type name.
    /// </summary>
    /// <param name="dto">Registration data using Name for both FirstName and LastName.</param>
    /// <returns>
    /// 200 OK with success message if all validations pass.
    /// 400 Bad Request with validation errors showing "FirstName" and "LastName" field names.
    /// </returns>
    [HttpPost("[action]")]
    public ActionResult<object> RegisterWithSharedNameType([FromBody] RegisterWithNameDto dto) =>
        // If we reach here, all value objects are validated.
        // The key test: both FirstName and LastName use the "Name" type,
        // but errors should show "FirstName" and "LastName" as field names.
        Ok(new
        {
            FirstName = dto.FirstName.Value,
            LastName = dto.LastName.Value,
            Email = dto.Email.Value,
            Message = "Validation passed - field names correctly attributed!"
        });
}