namespace Trellis.Asp.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Trellis;
using Trellis.Asp.ModelBinding;
using Xunit;

/// <summary>
/// Tests for <see cref="MaybePrimitiveModelBinder{T}"/> and the
/// <see cref="ScalarValueModelBinderProvider"/> extension that creates it for
/// <c>Maybe&lt;TPrimitive&gt;</c> parameters (route / query / header / form).
/// Closes the binder-side parity gap with <see cref="MaybeScalarValueJsonConverterFactory"/>
/// — same closed-primitive allowed list as the JSON converter factory.
/// </summary>
public class MaybePrimitiveModelBinderTests
{
    // ---------------------------------------------------------------------
    // long
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Maybe_long_binds_valid_value_as_Some()
    {
        var binder = new MaybePrimitiveModelBinder<long>();
        var ctx = CreateBindingContext("count", "42");

        await binder.BindModelAsync(ctx);

        ctx.Result.IsModelSet.Should().BeTrue();
        var m = ctx.Result.Model.Should().BeOfType<Maybe<long>>().Subject;
        m.HasValue.Should().BeTrue();
        m.Value.Should().Be(42L);
    }

    [Fact]
    public async Task Maybe_long_missing_value_binds_as_None()
    {
        var binder = new MaybePrimitiveModelBinder<long>();
        var ctx = CreateBindingContext("count", null);

        await binder.BindModelAsync(ctx);

        ctx.Result.IsModelSet.Should().BeTrue();
        ctx.Result.Model.Should().BeOfType<Maybe<long>>().Subject.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public async Task Maybe_long_empty_value_binds_as_None()
    {
        var binder = new MaybePrimitiveModelBinder<long>();
        var ctx = CreateBindingContext("count", "");

        await binder.BindModelAsync(ctx);

        ctx.Result.IsModelSet.Should().BeTrue();
        ctx.Result.Model.Should().BeOfType<Maybe<long>>().Subject.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public async Task Maybe_long_invalid_value_adds_model_state_error()
    {
        var binder = new MaybePrimitiveModelBinder<long>();
        var ctx = CreateBindingContext("count", "not-a-number");

        await binder.BindModelAsync(ctx);

        ctx.Result.IsModelSet.Should().BeFalse();
        ctx.ModelState.IsValid.Should().BeFalse();
        ctx.ModelState["count"]!.Errors.Should().NotBeEmpty();
    }

    // ---------------------------------------------------------------------
    // int / short / byte
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Maybe_int_binds_valid_value()
    {
        var binder = new MaybePrimitiveModelBinder<int>();
        var ctx = CreateBindingContext("n", "7");
        await binder.BindModelAsync(ctx);
        ctx.Result.Model.Should().BeOfType<Maybe<int>>().Subject.Value.Should().Be(7);
    }

    [Fact]
    public async Task Maybe_short_binds_valid_value()
    {
        var binder = new MaybePrimitiveModelBinder<short>();
        var ctx = CreateBindingContext("n", "100");
        await binder.BindModelAsync(ctx);
        ctx.Result.Model.Should().BeOfType<Maybe<short>>().Subject.Value.Should().Be((short)100);
    }

    [Fact]
    public async Task Maybe_byte_binds_valid_value()
    {
        var binder = new MaybePrimitiveModelBinder<byte>();
        var ctx = CreateBindingContext("flag", "200");
        await binder.BindModelAsync(ctx);
        ctx.Result.Model.Should().BeOfType<Maybe<byte>>().Subject.Value.Should().Be((byte)200);
    }

    // ---------------------------------------------------------------------
    // decimal / double / float
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Maybe_decimal_binds_valid_value()
    {
        var binder = new MaybePrimitiveModelBinder<decimal>();
        var ctx = CreateBindingContext("amount", "12.5");
        await binder.BindModelAsync(ctx);
        ctx.Result.Model.Should().BeOfType<Maybe<decimal>>().Subject.Value.Should().Be(12.5m);
    }

    [Fact]
    public async Task Maybe_double_binds_valid_value()
    {
        var binder = new MaybePrimitiveModelBinder<double>();
        var ctx = CreateBindingContext("score", "3.14");
        await binder.BindModelAsync(ctx);
        ctx.Result.Model.Should().BeOfType<Maybe<double>>().Subject.Value.Should().Be(3.14);
    }

    [Fact]
    public async Task Maybe_float_binds_valid_value()
    {
        var binder = new MaybePrimitiveModelBinder<float>();
        var ctx = CreateBindingContext("ratio", "0.5");
        await binder.BindModelAsync(ctx);
        ctx.Result.Model.Should().BeOfType<Maybe<float>>().Subject.Value.Should().Be(0.5f);
    }

    // ---------------------------------------------------------------------
    // string / bool / Guid / DateTime / DateTimeOffset
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Maybe_string_binds_valid_value()
    {
        var binder = new MaybePrimitiveModelBinder<string>();
        var ctx = CreateBindingContext("label", "hello");
        await binder.BindModelAsync(ctx);
        ctx.Result.Model.Should().BeOfType<Maybe<string>>().Subject.Value.Should().Be("hello");
    }

    [Fact]
    public async Task Maybe_bool_binds_valid_value()
    {
        var binder = new MaybePrimitiveModelBinder<bool>();
        var ctx = CreateBindingContext("enabled", "true");
        await binder.BindModelAsync(ctx);
        ctx.Result.Model.Should().BeOfType<Maybe<bool>>().Subject.Value.Should().BeTrue();
    }

    [Fact]
    public async Task Maybe_Guid_binds_valid_value()
    {
        var g = Guid.NewGuid();
        var binder = new MaybePrimitiveModelBinder<Guid>();
        var ctx = CreateBindingContext("id", g.ToString());
        await binder.BindModelAsync(ctx);
        ctx.Result.Model.Should().BeOfType<Maybe<Guid>>().Subject.Value.Should().Be(g);
    }

    [Fact]
    public async Task Maybe_DateTime_binds_valid_value()
    {
        var binder = new MaybePrimitiveModelBinder<DateTime>();
        var ctx = CreateBindingContext("when", "2026-05-15T10:30:00Z");
        await binder.BindModelAsync(ctx);
        ctx.Result.Model.Should().BeOfType<Maybe<DateTime>>().Subject.HasValue.Should().BeTrue();
    }

    [Fact]
    public async Task Maybe_DateTimeOffset_binds_valid_value()
    {
        var binder = new MaybePrimitiveModelBinder<DateTimeOffset>();
        var ctx = CreateBindingContext("when", "2026-05-15T12:00:00-07:00");
        await binder.BindModelAsync(ctx);
        ctx.Result.Model.Should().BeOfType<Maybe<DateTimeOffset>>().Subject.HasValue.Should().BeTrue();
    }

    // ---------------------------------------------------------------------
    // Provider extension — ScalarValueModelBinderProvider returns
    // MaybePrimitiveModelBinder<T> for Maybe<TPrimitive> shapes.
    // ---------------------------------------------------------------------

    [Fact]
    public void Provider_returns_MaybePrimitiveModelBinder_for_Maybe_long()
    {
        var provider = new ScalarValueModelBinderProvider();
        var ctx = CreateBinderProviderContext(typeof(Maybe<long>));
        var binder = provider.GetBinder(ctx);
        binder.Should().BeOfType<MaybePrimitiveModelBinder<long>>();
    }

    [Fact]
    public void Provider_returns_MaybePrimitiveModelBinder_for_Maybe_string()
    {
        var provider = new ScalarValueModelBinderProvider();
        var ctx = CreateBinderProviderContext(typeof(Maybe<string>));
        var binder = provider.GetBinder(ctx);
        binder.Should().BeOfType<MaybePrimitiveModelBinder<string>>();
    }

    [Fact]
    public void Provider_returns_null_for_Maybe_of_unsupported_primitive()
    {
        var provider = new ScalarValueModelBinderProvider();
        var ctx = CreateBinderProviderContext(typeof(Maybe<DateOnly>));
        var binder = provider.GetBinder(ctx);
        binder.Should().BeNull("DateOnly is not in the closed primitive allowed list");
    }

    [Fact]
    public void Provider_returns_null_for_Maybe_of_uint()
    {
        var provider = new ScalarValueModelBinderProvider();
        var ctx = CreateBinderProviderContext(typeof(Maybe<uint>));
        var binder = provider.GetBinder(ctx);
        binder.Should().BeNull("unsigned numerics are not in the allowed list");
    }

    // ---------------------------------------------------------------------
    // Test helpers — same pattern as MaybeModelBinderTests
    // ---------------------------------------------------------------------

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

    private static TestModelBinderProviderContext CreateBinderProviderContext(Type modelType)
    {
        var metadata = new TestModelMetadata(modelType);
        return new TestModelBinderProviderContext(metadata);
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

    private class TestModelMetadata : ModelMetadata
    {
        public TestModelMetadata(Type modelType)
            : base(ModelMetadataIdentity.ForType(modelType))
        {
        }

        public override IReadOnlyDictionary<object, object> AdditionalValues => new Dictionary<object, object>();
        public override ModelPropertyCollection Properties => new(Array.Empty<ModelMetadata>());
        public override string? BinderModelName => null;
        public override Type? BinderType => null;
        public override BindingSource? BindingSource => null;
        public override string? DataTypeName => null;
        public override string? Description => null;
        public override string? DisplayFormatString => null;
        public override string? DisplayName => null;
        public override string? EditFormatString => null;
        public override ModelMetadata? ElementMetadata => null;
        public override IEnumerable<KeyValuePair<EnumGroupAndName, string>>? EnumGroupedDisplayNamesAndValues => null;
        public override IReadOnlyDictionary<string, string>? EnumNamesAndValues => null;
        public override bool HasNonDefaultEditFormat => false;
        public override bool HideSurroundingHtml => false;
        public override bool HtmlEncode => true;
        public override bool IsBindingAllowed => true;
        public override bool IsBindingRequired => false;
        public override bool IsEnum => false;
        public override bool IsFlagsEnum => false;
        public override bool IsReadOnly => false;
        public override bool IsRequired => false;
        public override ModelBindingMessageProvider ModelBindingMessageProvider => new DefaultModelBindingMessageProvider();
        public override string? NullDisplayText => null;
        public override int Order => 0;
        public override string? Placeholder => null;
        public override ModelMetadata? ContainerMetadata => null;
        public override Func<object, object?>? PropertyGetter => null;
        public override Action<object, object?>? PropertySetter => null;
        public override bool ShowForDisplay => true;
        public override bool ShowForEdit => true;
        public override string? SimpleDisplayProperty => null;
        public override string? TemplateHint => null;
        public override bool ValidateChildren => true;
        public override IReadOnlyList<object> ValidatorMetadata => Array.Empty<object>();
        public override bool ConvertEmptyStringToNull => true;
        public override IPropertyFilterProvider? PropertyFilterProvider => null;
    }

    private class TestModelBinderProviderContext(ModelMetadata metadata) : ModelBinderProviderContext
    {
        private readonly ModelMetadata _metadata = metadata;

        public override BindingInfo BindingInfo => new();
        public override ModelMetadata Metadata => _metadata;
        public override IModelMetadataProvider MetadataProvider => new EmptyModelMetadataProvider();
        public override IModelBinder CreateBinder(ModelMetadata metadata) => throw new NotImplementedException();
    }
}