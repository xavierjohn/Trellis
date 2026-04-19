using Microsoft.AspNetCore.Routing;
using Trellis.Asp;
using Trellis.Primitives;

namespace SampleMinimalApiNoAot.API;

using SampleUserLibrary;
using Trellis;
using System.Globalization;

public static class UserRoutes
{
    public static void UseUserRoute(this WebApplication app)
    {
        RouteGroupBuilder userApi = app.MapGroup("/users");

        userApi.MapGet("/", () => "Hello Users - Reflection Fallback Version!");

        userApi.MapGet("/{name}", (string name) => $"Hello {name}").WithName("GetUserById");

        userApi.MapPost("/register", (RegisterUserRequest request) =>
            FirstName.TryCreate(request.firstName)
            .Combine(LastName.TryCreate(request.lastName))
            .Combine(EmailAddress.TryCreate(request.email))
            .Combine(PhoneNumber.TryCreate(request.phone))
            .Combine(Age.TryCreate(request.age))
            .Combine(CountryCode.TryCreate(request.country))
            .Bind((firstName, lastName, email, phone, age, country) =>
                User.TryCreate(firstName, lastName, email, phone, age, country, request.password))
            .ToHttpResult());

        userApi.MapPost("/registerCreated", (RegisterUserRequest request) =>
            FirstName.TryCreate(request.firstName)
            .Combine(LastName.TryCreate(request.lastName))
            .Combine(EmailAddress.TryCreate(request.email))
            .Combine(PhoneNumber.TryCreate(request.phone))
            .Combine(Age.TryCreate(request.age))
            .Combine(CountryCode.TryCreate(request.country))
            .Bind((firstName, lastName, email, phone, age, country) =>
                User.TryCreate(firstName, lastName, email, phone, age, country, request.password))
            .ToCreatedAtRouteHttpResult(
                    routeName: "GetUserById",
                    routeValues: user => new RouteValueDictionary { { "name", user.FirstName } }));

        userApi.MapGet("/notfound/{id}", (int id) =>
            Result.Fail(new Error.NotFound(new ResourceRef("Resource", id.ToString(CultureInfo.InvariantCulture))) { Detail = "User not found" })
            .ToHttpResult());

        userApi.MapGet("/conflict/{id}", (int id) =>
            Result.Fail(new Error.Conflict(null, id.ToString(CultureInfo.InvariantCulture)) { Detail = "Record has changed." })
            .ToHttpResult());

        userApi.MapGet("/forbidden/{id}", (int id) =>
            Result.Fail(new Error.Forbidden(id.ToString(CultureInfo.InvariantCulture)) { Detail = "You do not have access." })
            .ToHttpResult());

        userApi.MapGet("/unauthorized/{id}", (int id) =>
            Result.Fail(new Error.Unauthorized() { Detail = "You have not been authorized." })
            .ToHttpResult());

        userApi.MapGet("/unexpected/{id}", (int id) =>
            Result.Fail(new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = "Internal server error." })
            .ToHttpResult());

        // Auto-validating routes using value object DTOs
        // Works perfectly with reflection fallback - no source generator needed!
        // Demonstrates 7 different value objects being auto-validated:
        // FirstName, LastName, EmailAddress, PhoneNumber, Age, CountryCode, Url (optional)
        userApi.MapPost("/registerWithAutoValidation", (RegisterUserDto dto) =>
            User.TryCreate(
                dto.FirstName,
                dto.LastName,
                dto.Email,
                dto.Phone,
                dto.Age,
                dto.Country,
                dto.Password,
                dto.Website)
            .ToHttpResult())
            .WithScalarValueValidation();

        // Test that same value object type (Name) used for multiple properties
        // correctly reports validation errors with the property name, not the type name.
        // Reflection fallback handles this perfectly!
        userApi.MapPost("/registerWithSharedNameType", (RegisterWithNameDto dto) =>
            Results.Ok(new SharedNameTypeResponse(
                dto.FirstName.Value,
                dto.LastName.Value,
                dto.Email.Value,
                "Validation passed with reflection fallback - field names correctly attributed!")))
            .WithScalarValueValidation();
    }

}