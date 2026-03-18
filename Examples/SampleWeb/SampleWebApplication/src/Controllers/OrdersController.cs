namespace SampleWebApplication.Controllers;

using Microsoft.AspNetCore.Mvc;
using SampleUserLibrary;
using Trellis;

/// <summary>
/// Controller demonstrating RequiredEnum with ASP.NET Core MVC.
/// </summary>
[ApiController]
[Route("[controller]")]
public class OrdersController : ControllerBase
{
    /// <summary>
    /// Get all order states.
    /// </summary>
    [HttpGet("states")]
    public ActionResult GetStates() =>
        Ok(new
        {
            states = OrderState.GetAll().Select(s => new
            {
                name = s,
                canModify = s.CanModify,
                canCancel = s.CanCancel
            })
        });

    /// <summary>
    /// Get order state by name (tests model binding from route).
    /// </summary>
    [HttpGet("states/{state}")]
    public ActionResult GetStateByName(OrderState state) =>
        Ok(new
        {
            name = state,
            canModify = state.CanModify,
            canCancel = state.CanCancel,
            message = $"Successfully bound OrderState '{state}' from route!"
        });

    /// <summary>
    /// Update order (tests JSON body validation with RequiredEnum).
    /// </summary>
    [HttpPost("update")]
    public ActionResult UpdateOrder([FromBody] UpdateOrderDto dto) =>
        Ok(new
        {
            newState = dto.State,
            canModify = dto.State.CanModify,
            canCancel = dto.State.CanCancel,
            assignedTo = dto.AssignedTo.Match(name => name.Value, () => (string?)null),
            notes = dto.Notes,
            message = "Order state updated successfully!"
        });

    /// <summary>
    /// Create order (tests JSON body validation with RequiredEnum).
    /// </summary>
    [HttpPost("create")]
    public ActionResult CreateOrder([FromBody] CreateOrderDto dto) =>
        Ok(new
        {
            customer = new
            {
                firstName = dto.CustomerFirstName.Value,
                lastName = dto.CustomerLastName.Value,
                email = dto.CustomerEmail.Value
            },
            state = new
            {
                name = dto.InitialState,
                canModify = dto.InitialState.CanModify,
                canCancel = dto.InitialState.CanCancel
            },
            message = "Order created successfully with auto-validated RequiredEnum!"
        });

    /// <summary>
    /// Filter orders by state (tests query string binding).
    /// </summary>
    [HttpGet("filter")]
    public ActionResult FilterOrders([FromQuery] OrderState? state) =>
        state is null
            ? Ok(new { message = "No state filter provided" })
            : Ok(new
            {
                filterState = state,
                canModify = state.CanModify,
                message = $"Filtering orders by state: {state}"
            });
}