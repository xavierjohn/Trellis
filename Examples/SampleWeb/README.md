# Sample Web Applications

This folder contains three sample implementations demonstrating different ASP.NET Core patterns with Trellis. All three implementations expose identical endpoints and functionality but use different approaches.

## 🌐 Sample Projects

### Port Configuration

Each sample runs on a different port to allow side-by-side testing:

| Project | Port | Type | Features |
|---------|------|------|----------|
| **[SampleMinimalApi](SampleMinimalApi/)** | **5001** | Minimal API | ✅ Native AOT<br/>✅ Source Generator<br/>✅ Zero reflection |
| **[SampleMinimalApiNoAot](SampleMinimalApiNoAot/)** | **5002** | Minimal API | ✅ Reflection fallback<br/>✅ No source generator<br/>✅ Simplest setup |
| **[SampleWebApplication](SampleWebApplication/)** | **5003** | MVC Controllers | ✅ Full MVC<br/>✅ Swagger UI<br/>✅ Model binding |

## 🚀 Quick Start

### 1. Start One or More Samples

```bash
# Start SampleMinimalApi (Port 5001)
cd SampleMinimalApi
dotnet run

# Start SampleMinimalApiNoAot (Port 5002)
cd SampleMinimalApiNoAot
dotnet run

# Start SampleWebApplication (Port 5003)
cd SampleWebApplication/src
dotnet run
```

### 2. Test with OrderApi.http

Open `OrderApi.http` in Visual Studio or VS Code with REST Client extension.

Switch between implementations by uncommenting the desired `@host` variable:

```http
# Choose which sample API to test:
@host = http://localhost:5001   # SampleMinimalApi (AOT)
# @host = http://localhost:5002   # SampleMinimalApiNoAot (Reflection)
# @host = http://localhost:5003   # SampleWebApplication (MVC)
```

### 3. Verify Identical Behavior

Run the same tests against all three ports to verify they produce identical results!

## 📚 What's Demonstrated

### Common Features (All 3 Samples)

- ✅ **Automatic Value Object Validation** - 7 different value objects auto-validated
- ✅ **Manual Validation** - `Result.Combine()` for explicit control
- ✅ **Error Handling** - Problem Details (RFC 7807) responses
- ✅ **FluentValidation** - Business rules (e.g., age >= 18, password complexity)
- ✅ **Railway Oriented Programming** - `Bind`, `Tap`, `Ensure`, `Match`

### Value Objects Used

1. **FirstName** / **LastName** - Custom RequiredString types
2. **EmailAddress** - RFC 5322 compliant email validation
3. **PhoneNumber** - E.164 format validation
4. **Age** - Range validation (0-150, business rule >= 18)
5. **CountryCode** - ISO 3166-1 alpha-2 validation
6. **Url** - Optional HTTP/HTTPS URL validation
7. **UserId** - Custom RequiredGuid type

## 🎯 Key Endpoints

### User Registration

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/users/register` | POST | Manual validation with `Result.Combine()` |
| `/users/registerCreated` | POST | Same as above, returns 201 Created |
| `/users/RegisterWithAutoValidation` | POST | Automatic validation via model binding |

### Error Examples

| Endpoint | Expected Status |
|----------|----------------|
| `/users/notfound/{id}` | 404 Not Found |
| `/users/conflict/{id}` | 409 Conflict |
| `/users/forbidden/{id}` | 403 Forbidden |
| `/users/unauthorized/{id}` | 401 Unauthorized |
| `/users/unexpected/{id}` | 500 Internal Server Error |

### Todos (Basic CRUD)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/todos` | GET | Get all todos |
| `/todos/{id}` | GET | Get specific todo |

## 🔍 Comparing Implementations

### SampleMinimalApi (Port 5001)

**Best for:** Production deployment, Native AOT, maximum performance

```csharp
// Uses source generator for compile-time JSON serialization
[GenerateScalarValueConverters]
public partial class AppJsonSerializerContext : JsonSerializerContext { }

// Zero reflection overhead
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolver = AppJsonSerializerContext.Default);
```

### SampleMinimalApiNoAot (Port 5002)

**Best for:** Learning, prototyping, rapid development

```csharp
// Uses reflection fallback - no source generator needed
builder.Services.AddScalarValueValidationForMinimalApi();
app.UseScalarValueValidation();

// Just works - simplest setup!
```

### SampleWebApplication (Port 5003)

**Best for:** Traditional MVC applications, Swagger documentation

```csharp
// Full MVC with controllers
builder.Services
    .AddControllers()
    .AddScalarValueValidation();

// Swagger UI available at /swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
```

## 📖 Documentation

- **[Examples Guide](../EXAMPLES-GUIDE.md)** - Detailed comparison and when to use each
- **[OrderApi.http](OrderApi.http)** - Complete HTTP request collection
- **[Main README](../../README.md)** - Library overview and features

## 🧪 Testing

Run all requests in `OrderApi.http` against each port to verify:

1. ✅ **Identical validation behavior**
2. ✅ **Same error responses**
3. ✅ **Consistent status codes**
4. ✅ **Problem Details format**

This proves that all three approaches work identically - choose based on your deployment needs!

## 💡 Pro Tips

1. **Start with SampleMinimalApiNoAot (5002)** - Simplest to understand
2. **Use OrderApi.http** - All test cases pre-written
3. **Run multiple samples** - Compare behavior side-by-side
4. **Check Swagger** - SampleWebApplication (5003) has Swagger UI

## 🎓 Learning Path

1. **Understand basics** → Start with `/todos` endpoints
2. **Value object validation** → Try `/users/register` with invalid data
3. **Auto-validation** → Compare manual vs auto with `/users/RegisterWithAutoValidation`
4. **Error handling** → Test all error endpoints to see Problem Details
5. **Business rules** → Test age < 18 to see FluentValidation in action
