using Trellis.Asp;

namespace SampleMinimalApi.API;

using SampleUserLibrary;
using Trellis;

public static class OrderRoutes
{
    public static void UseOrderRoute(this WebApplication app)
    {
        RouteGroupBuilder orderApi = app.MapGroup("/orders");

        // GET all order states
        orderApi.MapGet("/states", () =>
            Results.Ok(new OrderStatesResponse(
                OrderState.GetAll().Select(s => new OrderStateInfo(
                    s.Value,
                    s.Ordinal,
                    s.CanModify,
                    s.CanCancel
                )).ToArray()
            )))
            .WithName("GetOrderStates");

        // GET order state by name (tests model binding from route)
        orderApi.MapGet("/states/{state}", (OrderState state) =>
            Results.Ok(new OrderStateDetailResponse(
                state.Value,
                state.Ordinal,
                state.CanModify,
                state.CanCancel,
                $"Successfully bound OrderState '{state}' from route!"
            )))
            .WithScalarValueValidation()
            .WithName("GetOrderStateByName");

        // POST update order (tests JSON body validation)
        orderApi.MapPost("/update", (UpdateOrderDto dto) =>
            Results.Ok(new UpdateOrderResponse(
                dto.State.Value,
                dto.State.CanModify,
                dto.State.CanCancel,
                dto.AssignedTo.Match(name => name.Value, () => (string?)null),
                dto.Notes,
                "Order state updated successfully!"
            )))
            .WithScalarValueValidation()
            .WithName("UpdateOrder");

        // POST create order (tests multiple value objects including RequiredEnum)
        orderApi.MapPost("/create", (CreateOrderDto dto) =>
            Results.Ok(new CreateOrderResponse(
                new CustomerInfo(
                    dto.CustomerFirstName.Value,
                    dto.CustomerLastName.Value,
                    dto.CustomerEmail.Value
                ),
                new OrderStateInfo(
                    dto.InitialState.Value,
                    dto.InitialState.Ordinal,
                    dto.InitialState.CanModify,
                    dto.InitialState.CanCancel
                ),
                "Order created successfully with auto-validated RequiredEnum!"
            )))
            .WithScalarValueValidation()
            .WithName("CreateOrder");

        // GET to test query string binding
        orderApi.MapGet("/filter", (OrderState? state) =>
            state is null
                ? Results.Ok(new FilterOrdersResponse(null, null, "No state filter provided"))
                : Results.Ok(new FilterOrdersResponse(
                    state.Value,
                    state.CanModify,
                    $"Filtering orders by state: {state}"
                )))
            .WithScalarValueValidation()
            .WithName("FilterOrders");
    }
}

// Response types for AOT compatibility
public record OrderStateInfo(string Value, int Ordinal, bool CanModify, bool CanCancel);
public record OrderStatesResponse(OrderStateInfo[] States);
public record OrderStateDetailResponse(string Value, int Ordinal, bool CanModify, bool CanCancel, string Message);
public record UpdateOrderResponse(string NewState, bool CanModify, bool CanCancel, string? AssignedTo, string? Notes, string Message);
public record CustomerInfo(string FirstName, string LastName, string Email);
public record CreateOrderResponse(CustomerInfo Customer, OrderStateInfo State, string Message);
public record FilterOrdersResponse(string? FilterState, bool? CanModify, string Message);