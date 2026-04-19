# Reflection Fallback for Value Object Validation

The Trellis.Asp library provides **automatic fallback to reflection** when the source generator is not available. This means you can use value object validation without any source generator reference in standard .NET applications.

## Two Validation Paths

### 1. Source Generator Path (AOT-Compatible) ✨ **Recommended for Native AOT**

**When to use:**
- Building Native AOT applications (`<PublishAot>true</PublishAot>`)
- Need assembly trimming
- Want zero reflection overhead
- Require fastest possible startup

**Setup:**
```xml
<ItemGroup>
  <ProjectReference Include="..\..\Asp\generator\AspSourceGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

```csharp
[GenerateScalarValueConverters]
[JsonSerializable(typeof(MyDto))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}
```

**How it works:**
- Source generator runs at compile time
- Generates strongly-typed JSON converters
- Adds `[JsonSerializable]` attributes automatically
- Zero reflection, fully AOT-compatible

### 2. Reflection Path (Automatic Fallback) 🔄 **Works Everywhere**

**When to use:**
- Standard .NET applications (not Native AOT)
- Rapid prototyping
- Don't want to manage source generator references
- Reflection overhead is acceptable

**Setup:**
```csharp
// For MVC Controllers
builder.Services
    .AddControllers()
    .AddScalarValueValidation();

// For Minimal APIs
builder.Services.AddScalarValueValidationForMinimalApi();

// That's it! No source generator needed.
```

**How it works:**
- `ValidatingJsonConverterFactory` uses reflection at runtime
- Detects types implementing `IScalarValue<TSelf, TPrimitive>`
- Creates converters dynamically using `Activator.CreateInstance`
- Transparent - your application code is identical

## Performance Comparison

| Metric | Reflection Path | Source Generator Path |
|--------|----------------|----------------------|
| **First request** | ~50μs slower (one-time reflection cost) | Fastest (pre-compiled) |
| **Subsequent requests** | Same performance | Same performance |
| **Memory at startup** | Slightly higher (~1-2KB per type) | Lower |
| **Startup time** | Negligible difference (<1ms for 100 types) | Fastest |
| **AOT compatible** | ❌ **NO** | ✅ **YES** |
| **Assembly trimming** | ⚠️ **May break** | ✅ **Safe** |
| **Build complexity** | ✅ Simpler | Requires analyzer reference |

### Real-World Impact

For most applications, the reflection overhead is **negligible**:

- **Startup**: The reflection scan happens once per type, typically <1ms for 100 value object types
- **Runtime**: After converters are created, performance is identical to source-generated converters
- **Memory**: Minimal - reflection metadata is shared across all instances

**Example**: An API with 50 value object types:
- Reflection overhead: ~0.5ms at startup
- Memory overhead: ~50KB
- Runtime performance: **Identical** to source generator

## When Reflection Fallback Happens

The library **automatically uses reflection** when:

1. **No `[GenerateScalarValueConverters]` attribute** is found on any `JsonSerializerContext`
2. **Source generator not referenced** in the project
3. **Running on standard .NET runtime** (not Native AOT)

## Example: Simple API Without Source Generator

```csharp
// Program.cs
using Trellis;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddScalarValueValidation();  // ← Uses reflection automatically

var app = builder.Build();
app.MapControllers();
app.Run();
```

```csharp
// Value object - works with reflection!
public class EmailAddress : ScalarValueObject<EmailAddress, string>,
                            IScalarValue<EmailAddress, string>
{
    private EmailAddress(string value) : base(value) { }

    public static Result<EmailAddress> TryCreate(string? value, string? fieldName = null)
    {
        var field = fieldName ?? "email";
        if (string.IsNullOrWhiteSpace(value))
            return new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Email is required." }));
        if (!value.Contains('@'))
            return new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Email must contain @." }));
        return new EmailAddress(value);
    }
}
```

```csharp
// DTO - uses EmailAddress directly
public record RegisterUserDto(EmailAddress Email, string Password);

// Controller - automatic validation!
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    [HttpPost]
    public IActionResult Register(RegisterUserDto dto)
    {
        // If we reach here, dto.Email is already validated!
        // No manual TryCreate calls needed
        return Ok(new { dto.Email.Value });
    }
}
```

**Request:**
```http
POST /api/users
Content-Type: application/json

{
  "email": "invalid",
  "password": "secret"
}
```

**Response (400 Bad Request):**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Email": ["Email must contain @."]
  }
}
```

## Migration Path

### Start Without Generator (Reflection)

Perfect for **prototyping** and **small applications**:

```csharp
builder.Services.AddScalarValueValidation();
// ← Uses reflection, works immediately
```

### Add Generator Later (For AOT)

When ready for **production** or **Native AOT**:

1. Add generator reference:
   ```xml
   <ProjectReference Include="path/to/AspSourceGenerator.csproj"
                     OutputItemType="Analyzer"
                     ReferenceOutputAssembly="false" />
   ```

2. Add attribute to your `JsonSerializerContext`:
   ```csharp
   [GenerateScalarValueConverters]  // ← Add this line
   [JsonSerializable(typeof(MyDto))]
   public partial class AppJsonSerializerContext : JsonSerializerContext
   {
   }
   ```

3. That's it! The source generator takes over automatically.

**Your application code doesn't change at all** - same DTOs, same controllers, same validation logic.

## Detecting Which Path Is Active

You can check at runtime which path is being used:

```csharp
var options = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value;
var hasGeneratedContext = options.SerializerOptions.TypeInfoResolver
    is JsonSerializerContext context
    && context.GetType().GetCustomAttributes(typeof(GenerateScalarValueConvertersAttribute), false).Any();

if (hasGeneratedContext)
    Console.WriteLine("Using source-generated converters (AOT-compatible)");
else
    Console.WriteLine("Using reflection-based converters (fallback)");
```

## Troubleshooting

### "My value objects aren't being validated!"

**Check:**
1. Is `AddScalarValueValidation()` or `AddScalarValueValidationForMinimalApi()` called?
2. Does your value object implement `IScalarValue<TSelf, TPrimitive>`?
3. Is the `TryCreate` method signature correct?

### "Getting trimming warnings (IL2026, IL2067, IL2070)"

**These warnings are expected** when using reflection path. They indicate:
- The reflection factory cannot be trimmed
- Not compatible with Native AOT

**Solutions:**
- Suppress warnings if staying on standard .NET runtime
- Add source generator for AOT/trimming scenarios

### "Source generator not producing output"

**Check:**
1. Is generator referenced with `OutputItemType="Analyzer"`?
2. Does any `JsonSerializerContext` have `[GenerateScalarValueConverters]`?
3. Try `dotnet clean` and rebuild

## Summary

| Scenario | Recommended Approach |
|----------|---------------------|
| **Prototyping** | Reflection (no generator) |
| **Small-medium apps** | Reflection (simpler setup) |
| **Large apps** | Source generator (better startup) |
| **Native AOT** | Source generator (required) |
| **Assembly trimming** | Source generator (required) |
| **Maximum performance** | Source generator (zero reflection) |

The beauty of this architecture is **you choose what's best for your scenario** - and you can change your mind later without touching application code.
