# Observability & Monitoring

**Level:** Advanced 🚀 | **Time:** 20-30 min | **Prerequisites:** [Basics](basics.md)

Enable distributed tracing and monitoring for Railway-Oriented Programming operations with OpenTelemetry and standard Problem Details error responses.

## Table of Contents

- [OpenTelemetry Tracing](#opentelemetry-tracing)

## OpenTelemetry Tracing

Enable distributed tracing for Railway Oriented Programming operations and Value Objects.

> **Important:** Auto-instrumentation (`AddResultsInstrumentation()`) traces **every** `Result<T>` operation and can create significant noise in production. Treat it as a break-glass debugging option for pipeline forensics, not as the default observability strategy.
>
> `AddPrimitiveValueObjectInstrumentation()` is often the more practical diagnostic default because it emits spans at value creation and validation boundaries, which usually carry higher signal with much lower volume.

### Installation

```bash
dotnet add package OpenTelemetry
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Extensions.Hosting
```

### Recommended: Manual Instrumentation

For production, manually instrument critical business operations:

```csharp
using System.Diagnostics;

public class OrderService
{
    private static readonly ActivitySource ActivitySource = new("MyApp.OrderService");

    public async Task<Result<Order>> ProcessOrderAsync(
        CreateOrderCommand command,
        CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("ProcessOrder");
        activity?.SetTag("order.customerId", command.CustomerId);
        activity?.SetTag("order.itemCount", command.Items.Count);

        var result = await _validator.ValidateToResultAsync(command, ct)
            .BindAsync((cmd, cancellationToken) => 
                CreateOrderAsync(cmd, cancellationToken), ct)
            .TapAsync(async (order, cancellationToken) => 
                await _repository.SaveAsync(order, cancellationToken), ct);

        // Record result in trace
        activity?.SetTag("result.isSuccess", result.IsSuccess);
        if (result.IsFailure)
        {
            activity?.SetTag("result.error.type", result.Error.GetType().Name);
            activity?.SetTag("result.error.detail", result.Error.Detail);
            activity?.SetStatus(ActivityStatusCode.Error, result.Error.Detail);
        }

        return result;
    }
}

// Register your ActivitySource
builder.Services.AddOpenTelemetry()
    .WithTracing(tracerBuilder =>
    {
        tracerBuilder
            .AddSource("MyApp.OrderService")  // Only trace what you register
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter();
    });
```

**Benefits of manual instrumentation:**
- ✅ **Control** - Trace only critical paths
- ✅ **Performance** - Minimal overhead
- ✅ **Signal-to-noise** - Clear, actionable traces
- ✅ **Business context** - Add domain-specific tags

### Auto-Instrumentation (Development/Debugging Only)

Auto-instrumentation is useful for **development and debugging** to see all ROP operations. In practice, the two built-in tracing modes serve different purposes:

- `AddResultsInstrumentation()` — best for short-lived deep debugging when you need to reconstruct an entire pipeline
- `AddPrimitiveValueObjectInstrumentation()` — often suitable for regular troubleshooting because value object creation/validation spans are fewer and easier to interpret

```csharp
// ⚠️ Development/debugging only - creates significant trace noise
builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .AddResultsInstrumentation()      // ⚠️ Traces EVERY Result<T> operation
            .AddPrimitiveValueObjectInstrumentation()      // ⚠️ Traces EVERY value object creation
            .AddConsoleExporter();  // Console output for debugging
    });
```

**Use auto-instrumentation when:**
- 🔍 Debugging complex ROP chains
- 🧪 Development/testing environments
- 📊 Analyzing performance bottlenecks
- 🐛 Troubleshooting specific issues

**Prefer primitive value object tracing when:**
- ✅ You want visibility into validation failures at API or message boundaries
- ✅ You need a lower-noise trace stream than full ROP pipeline tracing
- ✅ You care more about input quality and parsing failures than every intermediate pipeline step

**Avoid in production when:**
- ❌ High-traffic applications (performance overhead)
- ❌ Cost-sensitive environments (trace volume = $$$)
- ❌ Noise-sensitive monitoring (hard to find signal)

### Production Configuration

For production, use **selective instrumentation** with sampling:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: builder.Configuration["OpenTelemetry:ServiceName"] ?? "MyApp",
            serviceVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString()))
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            // ✅ Only register your own ActivitySources
            .AddSource("MyApp.OrderService")
            .AddSource("MyApp.UserService")
            .AddSource("MyApp.PaymentService")
            
            // ✅ Standard infrastructure instrumentation
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            
            // ✅ Sample to reduce overhead (10% of traces)
            .SetSampler(new TraceIdRatioBasedSampler(0.1))
            
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(
                    builder.Configuration["OpenTelemetry:Endpoint"] 
                    ?? "http://localhost:4317");
            });
    });
```

**Configuration (appsettings.json):**
```json
{
  "OpenTelemetry": {
    "ServiceName": "TrellisApi",
    "Endpoint": "https://otel-collector.example.com:4317"
  }
}
```

### Manual Instrumentation Patterns

#### Pattern 1: Command Handlers

```csharp
public class CreateUserCommandHandler
{
    private static readonly ActivitySource ActivitySource = new("MyApp.UserCommands");

    public async ValueTask<Result<User>> Handle(
        CreateUserCommand command,
        CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("CreateUser");
        activity?.SetTag("user.email", command.Email);
        
        var result = await _validator.ValidateToResultAsync(command, ct)
            .BindAsync((cmd, cancellationToken) => 
                User.CreateAsync(cmd, cancellationToken), ct)
            .TapAsync(async (user, cancellationToken) => 
                await _repository.SaveAsync(user, cancellationToken), ct);
        
        RecordResult(activity, result);
        return result;
    }

    private static void RecordResult<T>(Activity? activity, Result<T> result)
    {
        if (activity == null) return;
        
        activity.SetTag("result.isSuccess", result.IsSuccess);
        if (result.IsFailure)
        {
            activity.SetTag("result.error.type", result.Error.GetType().Name);
            activity.SetTag("result.error.code", result.Error.Code);
            activity.SetStatus(ActivityStatusCode.Error, result.Error.Detail);
        }
    }
}
```

#### Pattern 2: Domain Services

```csharp
public class PaymentService
{
    private static readonly ActivitySource ActivitySource = new("MyApp.Payments");

    public async Task<Result<Payment>> ProcessPaymentAsync(
        Order order,
        PaymentMethod method,
        CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("ProcessPayment");
        activity?.SetTag("payment.orderId", order.Id);
        activity?.SetTag("payment.amount", order.TotalAmount);
        activity?.SetTag("payment.method", method.ToString());

        var result = await ValidatePaymentMethod(method)
            .BindAsync(async (m, cancellationToken) => 
                await _gateway.ChargeAsync(order.TotalAmount, m, cancellationToken), ct)
            .TapAsync(async (payment, cancellationToken) => 
                await _repository.SaveAsync(payment, cancellationToken), ct);

        RecordResult(activity, result);
        return result;
    }
}
```

#### Pattern 3: Complex Workflows

```csharp
public class CheckoutWorkflow
{
    private static readonly ActivitySource ActivitySource = new("MyApp.Checkout");

    public async Task<Result<Order>> ExecuteAsync(
        CheckoutCommand command,
        CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("Checkout");
        activity?.SetTag("checkout.customerId", command.CustomerId);

        // Trace each step
        using var validateActivity = ActivitySource.StartActivity("ValidateCheckout");
        var validationResult = await ValidateCheckoutAsync(command, ct);
        RecordResult(validateActivity, validationResult);
        
        if (validationResult.IsFailure)
            return validationResult.Error;

        using var inventoryActivity = ActivitySource.StartActivity("CheckInventory");
        var inventoryResult = await CheckInventoryAsync(command.Items, ct);
        RecordResult(inventoryActivity, inventoryResult);
        
        if (inventoryResult.IsFailure)
            return inventoryResult.Error;

        using var paymentActivity = ActivitySource.StartActivity("ProcessPayment");
        var paymentResult = await ProcessPaymentAsync(command.Payment, ct);
        RecordResult(paymentActivity, paymentResult);

        // Continue workflow...
        return await CreateOrderAsync(command, ct);
    }
}
```

#### Pattern 4: Parallel Operations with Built-in Tracing

The `ParallelAsync` and `WhenAllAsync` operations automatically create tracing spans to monitor parallel execution:

```csharp
public class FraudDetectionService
{
    public async Task<Result<FraudCheckResult>> ValidateTransactionAsync(
        Transaction transaction,
        CancellationToken ct)
    {
        // ParallelAsync and WhenAllAsync automatically create "WhenAllAsync" spans
        // with parallel.task_count tags for observability
        return await Result.ParallelAsync(
            () => CheckBlacklistAsync(transaction.AccountId, ct),
            () => CheckVelocityLimitsAsync(transaction, ct),
            () => CheckAmountThresholdAsync(transaction, ct),
            () => CheckGeolocationAsync(transaction, ct)
        )
        .WhenAllAsync()  // Creates span: "WhenAllAsync" with tag "parallel.task_count: 4"
        .BindAsync((blacklist, velocity, amount, geo, cancellationToken) => 
            CombineChecksAsync(blacklist, velocity, amount, geo, cancellationToken), ct);
    }
}
```

**Automatic tracing benefits:**
- ✅ **Parallel task count** - `parallel.task_count` tag shows how many operations ran
- ✅ **Execution duration** - See how long parallel operations took
- ✅ **Success/failure status** - `ActivityStatusCode.Ok` or `ActivityStatusCode.Error`
- ✅ **Error correlation** - Failed operations show error status in traces

**Trace example in Jaeger/Application Insights:**
```
FraudDetectionService.ValidateTransaction (200ms)
  └─ WhenAllAsync (parallel.task_count: 4) (150ms)
     ├─ CheckBlacklist (50ms) ✓
     ├─ CheckVelocityLimits (100ms) ✓
     ├─ CheckAmountThreshold (30ms) ✓
     └─ CheckGeolocation (150ms) ✓
  └─ Bind: CombineChecks (50ms) ✓
