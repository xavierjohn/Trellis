using Trellis.Asp;

namespace SampleMinimalApiNoAot.API;

using SampleUserLibrary;
using Trellis;

public static class OrderRoutes
{
    public static void UseOrderRoute(this WebApplication app)
    {
        RouteGroupBuilder orderApi = app.MapGroup("/orders");

        // GET all order states
        orderApi.MapGet("/states", () =>
            Results.Ok(new
            {
                states = OrderState.GetAll().Select(s => new
                {
                    value = s.Value,
                    ordinal = s.Ordinal,
                    canModify = s.CanModify,
                    canCancel = s.CanCancel
                })
            }))
            .WithName("GetOrderStates");

        // GET order state by name (tests model binding from route)
        orderApi.MapGet("/states/{state}", (OrderState state) =>
            Results.Ok(new
            {
                value = state.Value,
                ordinal = state.Ordinal,
                canModify = state.CanModify,
                canCancel = state.CanCancel,
                message = $"Successfully bound OrderState '{state}' from route!"
            }))
            .WithScalarValueValidation()
            .WithName("GetOrderStateByName");

        // POST update order (tests JSON body validation)
        orderApi.MapPost("/update", (UpdateOrderDto dto) =>
            Results.Ok(new
            {
                newState = dto.State.Value,
                canModify = dto.State.CanModify,
                canCancel = dto.State.CanCancel,
                assignedTo = dto.AssignedTo.Match(name => name.Value, () => (string?)null),
                notes = dto.Notes,
                message = "Order state updated successfully!"
            }))
            .WithScalarValueValidation()
            .WithName("UpdateOrder");

        // POST create order (tests multiple value objects including RequiredEnum)
        orderApi.MapPost("/create", (CreateOrderDto dto) =>
            Results.Ok(new
            {
                customer = new
                {
                    firstName = dto.CustomerFirstName.Value,
                    lastName = dto.CustomerLastName.Value,
                    email = dto.CustomerEmail.Value
                },
                state = new
                {
                    value = dto.InitialState.Value,
                    ordinal = dto.InitialState.Ordinal,
                    canModify = dto.InitialState.CanModify,
                    canCancel = dto.InitialState.CanCancel
                },
                message = "Order created successfully with auto-validated RequiredEnum!"
            }))
            .WithScalarValueValidation()
            .WithName("CreateOrder");

        // GET to test query string binding
        orderApi.MapGet("/filter", (OrderState? state) =>
            state is null
                ? Results.Ok(new { message = "No state filter provided" })
                : Results.Ok(new
                {
                    filterState = state.Value,
                    canModify = state.CanModify,
                    message = $"Filtering orders by state: {state}"
                }))
            .WithScalarValueValidation()
            .WithName("FilterOrders");
    }
}