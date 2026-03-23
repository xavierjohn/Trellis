# Trellis Analyzers

The Trellis analyzers help you write safer, more functional code by detecting common mistakes and suggesting best practices for Railway Oriented Programming.

## Installation

```bash
dotnet add package Trellis.Analyzers
```

The analyzer package is automatically included when you install any Trellis package.

## Analyzer Rules

### Error Handling (TRLS001-TRLS006)

| ID | Title | Severity | Has Code Fix |
|----|-------|----------|--------------|
| [TRLS001](TRLS001.md) | Result return value is not handled | Warning | ❌ |
| [TRLS002](TRLS002.md) | Use Bind instead of Map when lambda returns Result | Info | ✅ |
| [TRLS003](TRLS003.md) | Unsafe access to Result.Value | Warning | ✅ |
| [TRLS004](TRLS004.md) | Unsafe access to Result.Error | Warning | ✅ |
| [TRLS005](TRLS005.md) | Consider using MatchError for error type discrimination | Info | ❌ |
| [TRLS006](TRLS006.md) | Unsafe access to Maybe.Value | Warning | ✅ |

### Value Objects (TRLS007)

| ID | Title | Severity | Has Code Fix |
|----|-------|----------|--------------|
| [TRLS007](TRLS007.md) | Use Create instead of TryCreate().Value | Warning | ✅ |

### Type Safety (TRLS008-TRLS011)

| ID | Title | Severity | Has Code Fix |
|----|-------|----------|--------------|
| [TRLS008](TRLS008.md) | Result is double-wrapped | Warning | ❌ |
| [TRLS009](TRLS009.md) | Incorrect async Result usage (blocking) | Warning | ❌ |
| [TRLS010](TRLS010.md) | Use specific error type instead of base Error class | Info | ❌ |
| [TRLS011](TRLS011.md) | Maybe is double-wrapped | Warning | ❌ |

### Code Quality (TRLS012-TRLS019)

| ID | Title | Severity | Has Code Fix |
|----|-------|----------|--------------|
| [TRLS012](TRLS012.md) | Consider using Result.Combine | Info | ❌ |
| [TRLS013](TRLS013.md) | Consider using GetValueOrDefault or Match | Info | ✅ |
| [TRLS014](TRLS014.md) | Use async method variant for async lambda | Warning | ✅ |
| [TRLS015](TRLS015.md) | Don't throw exceptions in Result chains | Warning | ❌ |
| [TRLS016](TRLS016.md) | Error message should not be empty | Warning | ❌ |
| [TRLS017](TRLS017.md) | Don't compare Result or Maybe to null | Warning | ❌ |
| [TRLS018](TRLS018.md) | Unsafe access to Value in LINQ expression | Warning | ❌ |
| [TRLS019](TRLS019.md) | Combine chain exceeds maximum supported tuple size | Error | ❌ |

### Entity Framework Core (TRLS020-TRLS021)

| ID | Title | Severity | Has Code Fix |
|----|-------|----------|--------------|
| [TRLS020](TRLS020.md) | Use SaveChangesResultAsync instead of SaveChangesAsync | Warning | ✅ |
| [TRLS021](TRLS021.md) | HasIndex references a Maybe\<T\> property | Warning | ❌ |

## Severity Levels

- **Error**: Code that will not work correctly and must be fixed
- **Warning**: Potential runtime errors or correctness issues that should be fixed
- **Info**: Suggestions for more idiomatic or maintainable code

## Code Fixes


Several analyzers provide automatic code fixes (✅ in the table above):

- **TRLS002**: Automatically replaces `Map` with `Bind`
- **TRLS003**: Wraps unsafe `result.Value` access in `if (result.IsSuccess)` guard
- **TRLS004**: Wraps unsafe `result.Error` access in `if (result.IsFailure)` guard
- **TRLS006**: Wraps unsafe `maybe.Value` access in `if (maybe.HasValue)` guard
- **TRLS007**: Replaces `TryCreate().Value` with `Create()`
- **TRLS013**: Replaces ternary with `GetValueOrDefault()` or `Match()`
- **TRLS014**: Replaces sync method with async variant (e.g., `Map` → `MapAsync`)
- **TRLS020**: Replaces `SaveChangesAsync`/`SaveChanges` with `SaveChangesResultUnitAsync` or `SaveChangesResultAsync`

## Configuration

### Disabling Specific Rules

You can disable specific rules in your `.editorconfig`:

```ini
# Disable TRLS003 for test projects
[**Tests/**/*.cs]
dotnet_diagnostic.TRLS003.severity = none

# Change TRLS007 to suggestion only
dotnet_diagnostic.TRLS007.severity = suggestion
```

### Suppressing Warnings

Use `#pragma` directives for local suppression:

```csharp
#pragma warning disable TRLS003
var customer = result.Value; // Intentionally unsafe in test
#pragma warning restore TRLS003
```

Or use `[SuppressMessage]` attribute:

```csharp
[SuppressMessage("Trellis", "TRLS003:Unsafe access to Result.Value",
    Justification = "Test code - validating success scenario")]
public void TestMethod()
{
    var customer = result.Value;
}
```

## Common Patterns

### API Controller Pattern

```csharp
[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateCustomer(CreateCustomerDto dto)
    {
        return await Result.Combine(
                EmailAddress.TryCreate(dto.Email),
                Name.TryCreate(dto.Name))
            .BindAsync((email, name) => 
                customerService.CreateAsync(email, name))
            .MapAsync(customer => customer.ToDto())
            .MatchAsync(
                onSuccess: dto => Ok(dto),
                onFailure: error => error.ToHttpResult());
    }
}
```

This pattern avoids all analyzer warnings!

## Feedback

Found a false positive or have suggestions? [Open an issue on GitHub](https://github.com/xavierjohn/Trellis/issues).
