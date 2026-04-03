# Trellis Examples Guide

This directory contains working examples demonstrating different aspects of the Trellis library.

## Quick Start - Which Example Should I Use?

### 🎯 New to Trellis? **Start here!**

**[SampleMinimalApiNoAot](SampleWeb/SampleMinimalApiNoAot/)** - Simplest setup with reflection fallback
- ✅ No source generator required
- ✅ No JsonSerializerContext needed
- ✅ Works out of the box
- ✅ Perfect for learning and prototyping
- ⚡ 50μs startup overhead (negligible)

### Native AOT Deployment?

**[SampleMinimalApi](SampleWeb/SampleMinimalApi/)** - AOT-optimized with source generator
- ✅ Native AOT compatible
- ✅ Zero reflection overhead
- ✅ Trimming-safe code
- ✅ Single-file executables
- ⚠️ Requires source generator setup

### Using MVC Controllers?

**[SampleWebApplication](SampleWeb/SampleWebApplication/)** - MVC with controllers
- ✅ Full MVC integration
- ✅ Model binding from route/query/form
- ✅ Action filters for validation
- ✅ [ApiController] attribute support

## Detailed Comparison

| Feature | [SampleMinimalApiNoAot](SampleWeb/SampleMinimalApiNoAot/) | [SampleMinimalApi](SampleWeb/SampleMinimalApi/) | [SampleWebApplication](SampleWeb/SampleWebApplication/) |
|---------|----------------------------------|--------------------------|------------------------------|
| **Type** | Minimal API | Minimal API | MVC Controllers |
| **Source Generator** | ❌ Not needed | ✅ Required | ❌ Optional |
| **JsonSerializerContext** | ❌ Not needed | ✅ Required | ❌ Not needed |
| **PublishAot** | ❌ No | ✅ Yes | ❌ No |
| **Setup Complexity** | ⭐ Simple | ⭐⭐ Moderate | ⭐⭐ Moderate |
| **Startup Time** | Fast (+50μs) | Fastest | Fast |
| **Runtime Performance** | Identical | Identical | Identical |
| **Best For** | Most apps, learning | AOT deployment | MVC apps |
| **Recommended For** | Beginners ✅ | Advanced deployment | MVC users |

## All Examples

### Core Examples

#### [SampleMinimalApiNoAot](SampleWeb/SampleMinimalApiNoAot/) **← Start here!**
**Perfect for: Learning, prototyping, most production APIs**

Demonstrates that the library works perfectly without source generation using automatic reflection fallback.

**Key Features:**
- Simple setup (3 lines of code)
- No source generator needed
- All features work identically
- Comprehensive README with performance analysis
- Test endpoints included (.http file)

**When to use:**
- ✅ Learning the library
- ✅ Prototyping new features
- ✅ Standard .NET applications
- ✅ Most production APIs (reflection overhead is negligible)
- ✅ When you want zero friction setup

#### [SampleMinimalApi](SampleWeb/SampleMinimalApi/)
**Perfect for: Native AOT deployment, maximum performance**

Shows how to use the source generator for Native AOT compilation and zero reflection overhead.

**Key Features:**
- Native AOT compatible
- Source generator for compile-time code generation
- JsonSerializerContext for AOT-safe JSON
- Zero reflection overhead
- Trimming-safe code

**When to use:**
- ✅ Native AOT deployment required
- ✅ Single-file executables
- ✅ Maximum startup performance critical
- ✅ Container images (smaller size)
- ✅ Cloud-native deployments

#### [SampleWebApplication](SampleWeb/SampleWebApplication/)
**Perfect for: MVC applications with controllers**

Demonstrates full MVC integration with controllers and action filters.

**Key Features:**
- MVC Controllers with [ApiController]
- Model binding from route/query/form/headers
- Action filters for automatic validation
- Integration with ASP.NET Core validation
- Result-to-ActionResult conversion

**When to use:**
- ✅ Using MVC Controllers (not Minimal APIs)
- ✅ Need model binding from multiple sources
- ✅ Want action filter integration
- ✅ Using [ApiController] attribute

### Supporting Libraries

#### [SampleUserLibrary](SampleUserLibrary/)
Shared library with value objects used by all examples.

**Contains:**
- `EmailAddress` - Email validation
- `FirstName`, `LastName` - Name validation
- `Name` - Generic name (tests shared type attribution)
- `User` - Domain entity
- Request/Response DTOs

**Used by:** All example applications

## Feature Matrix

| Feature | NoAot | AOT | MVC |
|---------|-------|-----|-----|
| **Value Object Validation** | ✅ | ✅ | ✅ |
| **JSON Deserialization** | ✅ Reflection | ✅ Generated | ✅ Reflection |
| **Error Collection** | ✅ | ✅ | ✅ |
| **Property-Aware Errors** | ✅ | ✅ | ✅ |
| **Result Conversion** | ✅ | ✅ | ✅ |
| **Model Binding** | ⚠️ JSON only | ⚠️ JSON only | ✅ All sources |
| **Action Filters** | ⚠️ Endpoint filters | ⚠️ Endpoint filters | ✅ Action filters |
| **Native AOT** | ❌ | ✅ | ❌ |
| **Maybe&lt;T&gt; Optional Props** | ✅ | ✅ | ✅ |
| **Startup Overhead** | +50μs | 0μs | +50μs |
| **EF Core Persistence** | ✅ | ✅ | ✅ |
| **ETag / Conditional Requests** | ✅ | ✅ | ✅ |
| **Pagination (206 + Content-Range)** | ✅ | ✅ | ✅ |
| **Prefer Header (RFC 7240)** | ✅ | ✅ | ✅ |
| **Async ROP Chains** | ✅ | ✅ | ✅ |
| **ParallelAsync / WhenAllAsync** | ✅ | ✅ | ✅ |
| **RecoverOnFailureAsync** | ✅ | ✅ | ✅ |
| **Authorization (Actor)** | ✅ | ✅ | ✅ |
| **Specification Pattern** | ✅ | ✅ | ✅ |
| **Aggregate State Machine** | ✅ | ✅ | ✅ |
| **RFC 9110 Redirects** | ✅ | ✅ | ✅ |

## Decision Tree

```
Are you new to the library?
├─ Yes → SampleMinimalApiNoAot ✅
└─ No
   ├─ Need Native AOT?
   │  ├─ Yes → SampleMinimalApi
   │  └─ No
   │     ├─ Using MVC Controllers?
   │     │  ├─ Yes → SampleWebApplication
   │     │  └─ No → SampleMinimalApiNoAot
   │     └─ Using Minimal APIs?
   │        └─ SampleMinimalApiNoAot ✅
```

## Running the Examples

### SampleMinimalApiNoAot
```bash
cd Examples/SampleMinimalApiNoAot
dotnet run

# Visit http://localhost:5000/users
# Test with SampleMinimalApiNoAot.http file
```

### SampleMinimalApi
```bash
cd Examples/SampleMinimalApi
dotnet run

# Visit http://localhost:5000/users
```

### SampleWebApplication
```bash
cd Examples/SampleWebApplication
dotnet run

# Visit http://localhost:5000/api/users
```

## Common Scenarios

### "I want to learn the library"
**→ [SampleMinimalApiNoAot](SampleMinimalApiNoAot/)**
- Simplest setup
- No prerequisites
- Comprehensive README

### "I need to deploy to Native AOT"
**→ [SampleMinimalApi](SampleMinimalApi/)**
- Shows source generator setup
- Explains JsonSerializerContext
- AOT deployment ready

### "I'm building an MVC application"
**→ [SampleWebApplication](SampleWebApplication/)**
- Controllers with [ApiController]
- Model binding examples
- Action filter integration

### "I want the best performance"
**→ Both NoAot and AOT have identical runtime performance!**
- NoAot: +50μs startup (one-time)
- AOT: 0μs startup overhead
- Runtime: Identical for both

For 99% of applications, the 50μs startup difference is negligible.

### "I'm prototyping a new API"
**→ [SampleMinimalApiNoAot](SampleMinimalApiNoAot/)**
- Zero friction setup
- Add source generator later if needed
- No code changes when migrating

## Migration Between Examples

### From NoAot → AOT
1. Add generator reference to .csproj
2. Add `[GenerateScalarValueConverters]` to JsonSerializerContext
3. Add `<PublishAot>true</PublishAot>`
4. **No endpoint code changes needed!**

### From AOT → NoAot
1. Remove generator reference from .csproj
2. Remove `[GenerateScalarValueConverters]` attribute
3. Remove `<PublishAot>true</PublishAot>`
4. **No endpoint code changes needed!**

### From Minimal API → MVC
1. Add Controllers
2. Change service registration to `AddControllers()`
3. Use `ToActionResult()` instead of `ToHttpResult()`
4. Add action filters instead of endpoint filters

## Testing

All examples include:
- ✅ `.http` files for manual testing
- ✅ Sample value objects
- ✅ Valid and invalid request examples
- ✅ Error response examples

Test with:
- Visual Studio's HTTP Client
- VS Code REST Client extension
- curl, Postman, or any HTTP client

## Documentation

Each example includes:
- **README.md** - Detailed documentation
- **Code comments** - Inline explanations
- **.http file** - Request/response examples

### Additional Resources
- **[Asp/README.md](../Trellis.Asp/README.md)** - Main library documentation
- **[Asp/docs/REFLECTION-FALLBACK.md](../Trellis.Asp/docs/REFLECTION-FALLBACK.md)** - Reflection vs AOT deep dive
- **[Asp/generator/README.md](../Trellis.Asp/generator/README.md)** - Source generator details

## FAQ

### Q: Which example should I start with?
**A: [SampleMinimalApiNoAot](SampleMinimalApiNoAot/)** - Simplest setup, works for 99% of cases.

### Q: Do I need the source generator?
**A: No!** The library works perfectly without it using reflection fallback.

### Q: What's the performance difference between reflection and AOT?
**A: 50μs on first request** (one-time reflection cost). Runtime performance is identical.

### Q: Can I migrate from reflection to AOT later?
**A: Yes!** No code changes needed in your endpoints.

### Q: Which is faster at runtime?
**A: Both are identical** - the difference is only in startup/first-request time.

### Q: Should I use Minimal APIs or MVC?
**A: Your choice!** Both work great with Trellis.

### Q: Can I use this in production without the source generator?
**A: Absolutely!** The reflection fallback is production-ready.

## Recommended Learning Path

1. **Start:** [SampleMinimalApiNoAot](SampleMinimalApiNoAot/) - Learn the basics
2. **Explore:** [SampleWebApplication](SampleWebApplication/) - See MVC integration
3. **Advanced:** [SampleMinimalApi](SampleMinimalApi/) - Understand AOT deployment
4. **Deep dive:** [Asp/docs/REFLECTION-FALLBACK.md](../Trellis.Asp/docs/REFLECTION-FALLBACK.md)

## Contributing

Found an issue or want to add an example?
- Open an issue at [github.com/xavierjohn/Trellis/issues](https://github.com/xavierjohn/Trellis/issues)
- Examples should be simple, focused, and well-documented

## License

Part of the Trellis library. See [LICENSE](../LICENSE) for details.
