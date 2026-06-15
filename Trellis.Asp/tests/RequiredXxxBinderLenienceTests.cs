namespace Trellis.Asp.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Trellis;
using Trellis.Asp.ModelBinding;
using Trellis.Testing;
using Xunit;

// Both fixtures are bare lenient under the post-flip defaults; the test names retain their
// historical "Lenient" / "Strict" labels but assertions all express the lenient binder behavior.
public partial class AspLenientGuid : RequiredGuid<AspLenientGuid> { }

public partial class AspStrictGuid : RequiredGuid<AspStrictGuid> { }

public partial class AspLenientString : RequiredString<AspLenientString> { }

public partial class AspStrictString : RequiredString<AspStrictString> { }

public partial class AspLenientInt : RequiredInt<AspLenientInt> { }

public partial class AspStrictInt : RequiredInt<AspStrictInt> { }

/// <summary>
/// ASP-boundary regression coverage for the post-flip Required* lenient defaults: proves that
/// the bare Required* base accepts the per-type sentinel via
/// <see cref="ScalarValueModelBinder{TValue, TPrimitive}"/> on the route / query / form / header
/// path.
/// </summary>
/// <remarks>
/// Mirrors the EF rehydration test (<c>RequiredXxxRehydrationLenienceTests</c>) and the
/// composite flow-through test (<c>CompositeRequiredStringFlowThroughTests</c>) at the ASP
/// model-binder boundary. JSON-body coverage (via <c>ParsableJsonConverter&lt;T&gt;</c>) is
/// already exercised by the composite flow-through tests since the composite converter
/// delegates through the same inner <c>TryCreate</c>.
/// </remarks>
public class RequiredXxxBinderLenienceTests
{
    // ---- Guid ----

    [Fact]
    public async Task LenientGuidBinder_accepts_all_zero_guid()
    {
        var binder = new ScalarValueModelBinder<AspLenientGuid, Guid>();
        var ctx = CreateBindingContext("id", "00000000-0000-0000-0000-000000000000");

        await binder.BindModelAsync(ctx);

        ctx.Result.IsModelSet.Should().BeTrue();
        var bound = ctx.Result.Model as AspLenientGuid;
        bound.Should().NotBeNull();
        bound!.Value.Should().Be(Guid.Empty);
        ctx.ModelState.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task StrictGuidBinder_accepts_all_zero_guid()
    {
        var binder = new ScalarValueModelBinder<AspStrictGuid, Guid>();
        var ctx = CreateBindingContext("id", "00000000-0000-0000-0000-000000000000");

        await binder.BindModelAsync(ctx);

        ctx.Result.IsModelSet.Should().BeTrue();
        var bound = ctx.Result.Model as AspStrictGuid;
        bound!.Value.Should().Be(Guid.Empty);
        ctx.ModelState.ErrorCount.Should().Be(0);
    }

    // ---- String ----

    [Fact]
    public async Task LenientStringBinder_accepts_empty_string()
    {
        var binder = new ScalarValueModelBinder<AspLenientString, string>();
        var ctx = CreateBindingContext("name", "");

        await binder.BindModelAsync(ctx);

        // Note: model-binder treats "" as "no value" at the value-provider layer for strings
        // and short-circuits before reaching TryCreate. This is documented existing behavior
        // (see ModelBinder_NoValue_DoesNotBind in ModelBindingTests). The point of this test
        // is to confirm the lenient TryCreate does not reject empty when called directly:
        AspLenientString.TryCreate("").IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task StrictStringBinder_accepts_empty_string_via_direct_TryCreate()
    {
        // Under the lenient default the bare Required* base accepts "" verbatim.
        var result = AspStrictString.TryCreate("");
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be("");

        // And via binder for a whitespace-only payload — bound verbatim.
        var binder = new ScalarValueModelBinder<AspStrictString, string>();
        var ctx = CreateBindingContext("name", "   ");

        await binder.BindModelAsync(ctx);

        ctx.Result.IsModelSet.Should().BeTrue();
        var bound = ctx.Result.Model as AspStrictString;
        bound!.Value.Should().Be("   ");
        ctx.ModelState.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task LenientStringBinder_accepts_whitespace_verbatim()
    {
        var binder = new ScalarValueModelBinder<AspLenientString, string>();
        var ctx = CreateBindingContext("name", "  hi  ");

        await binder.BindModelAsync(ctx);

        ctx.Result.IsModelSet.Should().BeTrue();
        var bound = ctx.Result.Model as AspLenientString;
        bound!.Value.Should().Be("  hi  ");
    }

    // ---- Int ----

    [Fact]
    public async Task LenientIntBinder_accepts_zero()
    {
        var binder = new ScalarValueModelBinder<AspLenientInt, int>();
        var ctx = CreateBindingContext("count", "0");

        await binder.BindModelAsync(ctx);

        ctx.Result.IsModelSet.Should().BeTrue();
        var bound = ctx.Result.Model as AspLenientInt;
        bound!.Value.Should().Be(0);
        ctx.ModelState.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task StrictIntBinder_accepts_zero()
    {
        var binder = new ScalarValueModelBinder<AspStrictInt, int>();
        var ctx = CreateBindingContext("count", "0");

        await binder.BindModelAsync(ctx);

        ctx.Result.IsModelSet.Should().BeTrue();
        var bound = ctx.Result.Model as AspStrictInt;
        bound!.Value.Should().Be(0);
        ctx.ModelState.ErrorCount.Should().Be(0);
    }

    // ---- Helpers ----

    private static DefaultModelBindingContext CreateBindingContext(string modelName, string? value)
    {
        var valueProvider = new SimpleValueProvider();
        if (value is not null)
            valueProvider.Add(modelName, value);

        return new DefaultModelBindingContext
        {
            ModelName = modelName,
            ValueProvider = valueProvider,
            ModelState = new ModelStateDictionary(),
        };
    }

    private class SimpleValueProvider : IValueProvider
    {
        private readonly Dictionary<string, string> _values = new();

        public void Add(string key, string value) => _values[key] = value;

        public bool ContainsPrefix(string prefix) => _values.ContainsKey(prefix);

        public ValueProviderResult GetValue(string key) =>
            _values.TryGetValue(key, out var value)
                ? new ValueProviderResult(value)
                : ValueProviderResult.None;
    }
}