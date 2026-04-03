namespace SampleWebApplication.Controllers;

using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("")]
public class HomeController : ControllerBase
{
    private static readonly string[] ErrorEndpoints =
    [
        "GET /users/notfound/{id} - Returns 404 Not Found",
        "GET /users/conflict/{id} - Returns 409 Conflict",
        "GET /users/forbidden/{id} - Returns 403 Forbidden",
        "GET /users/unauthorized/{id} - Returns 401 Unauthorized",
        "GET /users/unexpected/{id} - Returns 500 Internal Server Error"
    ];

    [HttpGet]
    public ActionResult<WelcomeResponse> Welcome() => Ok(new WelcomeResponse(
        Name: "FunctionalDDD Sample Web Application",
        Version: "2.0.0",
        Description: "Demonstrates Trellis Framework with ROP, EF Core, RFC 9110, and authorization using MVC Controllers",
        Endpoints: new EndpointsInfo(
            Users: new UserEndpoints(
                Register: "POST /users/register - Register user with manual validation (Result.Combine)",
                RegisterCreated: "POST /users/registerCreated - Register user returning 201 Created",
                RegisterAutoValidation: "POST /users/RegisterWithAutoValidation - Auto-validation (Maybe<Url>)",
                Errors: ErrorEndpoints
            ),
            Products: new ProductEndpoints(
                List: "GET /products?page=0&pageSize=25&inStock=true&minPrice=50&maxPrice=200 - Paginated (RFC 9110 §14: 206 Partial Content)",
                GetById: "GET /products/{id} - Conditional GET (If-None-Match → 304 Not Modified)",
                Create: "POST /products - Create with ETag + Location",
                Update: "PUT /products/{id} - Update with If-Match (Prefer: return=minimal → 204)",
                Delete: "DELETE /products/{id} - Delete (204 No Content)",
                LegacyRedirect: "GET /products/legacy/{id} - 301 Moved Permanently redirect"
            ),
            Orders: new OrderEndpoints(
                Create: "POST /orders - Create order (async BindAsync chain)",
                GetById: "GET /orders/{id} - Get with ETag conditional GET",
                Confirm: "POST /orders/{id}/confirm - Confirm (EnsureAsync + BindAsync + TapAsync + auth)",
                Cancel: "POST /orders/{id}/cancel - Cancel (RecoverOnFailureAsync cleanup)",
                Receipt: "POST /orders/{id}/receipt - 303 See Other redirect",
                States: "GET /orders/states - All order states (RequiredEnum demo)",
                StateByName: "GET /orders/states/{state} - State model binding"
            ),
            Dashboard: "GET /dashboard - ParallelAsync/WhenAllAsync concurrent data fetch",
            Authorization: "Set X-Test-Actor header: {\"id\":\"user1\",\"permissions\":[\"orders:write\"]}"
        ),
        Documentation: "See SampleApi.http for complete API examples"
    ));
}

public record WelcomeResponse(string Name, string Version, string Description, EndpointsInfo Endpoints, string Documentation);
public record EndpointsInfo(UserEndpoints Users, ProductEndpoints Products, OrderEndpoints Orders, string Dashboard, string Authorization);
public record UserEndpoints(string Register, string RegisterCreated, string RegisterAutoValidation, string[] Errors);
public record ProductEndpoints(string List, string GetById, string Create, string Update, string Delete, string LegacyRedirect);
public record OrderEndpoints(string Create, string GetById, string Confirm, string Cancel, string Receipt, string States, string StateByName);