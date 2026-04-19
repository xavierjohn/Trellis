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
/// Tests for <see cref="MaybeModelBinder{TValue, TPrimitive}"/> and
/// <see cref="ScalarValueModelBinderProvider"/> Maybe support.
/// Validates model binding of <see cref="Maybe{T}"/> from route/query/form/header parameters.
/// </summary>
public class MaybeModelBinderTests
{
    #region Test Value Objects

    public class UserId : ScalarValueObject<UserId, Guid>, IScalarValue<UserId, Guid>
    {
        private UserId(Guid value) : base(value) { }

        public static Result<UserId> TryCreate(Guid value, string? fieldName = null)
        {
            var field = fieldName ?? "userId";
            if (value == Guid.Empty)
                return Result.Fail<Asp.Tests.MaybeModelBinderTests.UserId>(Error.Validation("UserId cannot be empty.", field));
            return Result.Ok(new UserId(value));
        }

        public static Result<UserId> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public class ProductCode : ScalarValueObject<ProductCode, string>, IScalarValue<ProductCode, string>
    {
        private ProductCode(string value) : base(value) { }

        public static Result<ProductCode> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "productCode";
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<Asp.Tests.MaybeModelBinderTests.ProductCode>(Error.Validation("ProductCode is required.", field));
            if (value.Length < 3)
                return Result.Fail<Asp.Tests.MaybeModelBinderTests.ProductCode>(Error.Validation("ProductCode must be at least 3 characters.", field));
            return Result.Ok(new ProductCode(value));
        }
    }

    public class Quantity : ScalarValueObject<Quantity, int>, IScalarValue<Quantity, int>
    {
        private Quantity(int value) : base(value) { }

        public static Result<Quantity> TryCreate(int value, string? fieldName = null)
        {
            var field = fieldName ?? "quantity";
            if (value <= 0)
                return Result.Fail<Asp.Tests.MaybeModelBinderTests.Quantity>(Error.Validation("Quantity must be greater than zero.", field));
            if (value > 1000)
                return Result.Fail<Asp.Tests.MaybeModelBinderTests.Quantity>(Error.Validation("Quantity cannot exceed 1000.", field));
            return Result.Ok(new Quantity(value));
        }

        public static Result<Quantity> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public class Price : ScalarValueObject<Price, decimal>, IScalarValue<Price, decimal>
    {
        private Price(decimal value) : base(value) { }

        public static Result<Price> TryCreate(decimal value, string? fieldName = null)
        {
            var field = fieldName ?? "price";
            if (value < 0)
                return Result.Fail<Asp.Tests.MaybeModelBinderTests.Price>(Error.Validation("Price cannot be negative.", field));
            return Result.Ok(new Price(value));
        }

        public static Result<Price> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    #endregion

    #region MaybeModelBinder — Value Absent

    [Fact]
    public async Task MaybeBinder_NoValue_ReturnsMaybeNone()
    {
        // Arrange
        var binder = new MaybeModelBinder<ProductCode, string>();
        var context = CreateBindingContext("code", null);

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeTrue("absent value should set result to Maybe.None");
        var maybe = context.Result.Model.Should().BeOfType<Maybe<ProductCode>>().Subject;
        maybe.HasNoValue.Should().BeTrue();
        context.ModelState.ErrorCount.Should().Be(0, "absent optional parameter is not an error");
    }

    [Fact]
    public async Task MaybeBinder_EmptyString_ReturnsMaybeNone()
    {
        // Arrange
        var binder = new MaybeModelBinder<ProductCode, string>();
        var context = CreateBindingContext("code", "");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeTrue();
        var maybe = context.Result.Model.Should().BeOfType<Maybe<ProductCode>>().Subject;
        maybe.HasNoValue.Should().BeTrue("empty string for optional param means 'not provided'");
        context.ModelState.ErrorCount.Should().Be(0);
    }

    #endregion

    #region MaybeModelBinder — String Valid/Invalid

    [Fact]
    public async Task MaybeBinder_ValidString_ReturnsMaybeWithValue()
    {
        // Arrange
        var binder = new MaybeModelBinder<ProductCode, string>();
        var context = CreateBindingContext("code", "ABC123");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeTrue();
        var maybe = context.Result.Model.Should().BeOfType<Maybe<ProductCode>>().Subject;
        maybe.HasValue.Should().BeTrue();
        maybe.Value.Value.Should().Be("ABC123");
        context.ModelState.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task MaybeBinder_InvalidString_AddsValidationError()
    {
        // Arrange — ProductCode requires minimum 3 characters
        var binder = new MaybeModelBinder<ProductCode, string>();
        var context = CreateBindingContext("code", "AB");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        context.ModelState["code"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("ProductCode must be at least 3 characters.");
    }

    #endregion

    #region MaybeModelBinder — Guid Valid/Invalid

    [Fact]
    public async Task MaybeBinder_ValidGuid_ReturnsMaybeWithValue()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var binder = new MaybeModelBinder<UserId, Guid>();
        var context = CreateBindingContext("userId", guid.ToString());

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeTrue();
        var maybe = context.Result.Model.Should().BeOfType<Maybe<UserId>>().Subject;
        maybe.HasValue.Should().BeTrue();
        maybe.Value.Value.Should().Be(guid);
        context.ModelState.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task MaybeBinder_EmptyGuid_AddsValidationError()
    {
        // Arrange
        var binder = new MaybeModelBinder<UserId, Guid>();
        var context = CreateBindingContext("userId", Guid.Empty.ToString());

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        context.ModelState["userId"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("UserId cannot be empty.");
    }

    [Fact]
    public async Task MaybeBinder_InvalidGuid_ReturnsParseError()
    {
        // Arrange
        var binder = new MaybeModelBinder<UserId, Guid>();
        var context = CreateBindingContext("userId", "not-a-guid");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        context.ModelState["userId"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("is not a valid GUID");
    }

    #endregion

    #region MaybeModelBinder — Int Valid/Invalid

    [Fact]
    public async Task MaybeBinder_ValidInt_ReturnsMaybeWithValue()
    {
        // Arrange
        var binder = new MaybeModelBinder<Quantity, int>();
        var context = CreateBindingContext("quantity", "42");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeTrue();
        var maybe = context.Result.Model.Should().BeOfType<Maybe<Quantity>>().Subject;
        maybe.HasValue.Should().BeTrue();
        maybe.Value.Value.Should().Be(42);
        context.ModelState.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task MaybeBinder_IntBelowMinimum_AddsValidationError()
    {
        // Arrange
        var binder = new MaybeModelBinder<Quantity, int>();
        var context = CreateBindingContext("quantity", "0");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        context.ModelState["quantity"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("Quantity must be greater than zero.");
    }

    [Fact]
    public async Task MaybeBinder_InvalidInt_ReturnsParseError()
    {
        // Arrange
        var binder = new MaybeModelBinder<Quantity, int>();
        var context = CreateBindingContext("quantity", "not-a-number");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        context.ModelState["quantity"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("is not a valid integer");
    }

    #endregion

    #region MaybeModelBinder — Decimal Valid/Invalid

    [Fact]
    public async Task MaybeBinder_ValidDecimal_ReturnsMaybeWithValue()
    {
        // Arrange
        var binder = new MaybeModelBinder<Price, decimal>();
        var context = CreateBindingContext("price", "99.99");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeTrue();
        var maybe = context.Result.Model.Should().BeOfType<Maybe<Price>>().Subject;
        maybe.HasValue.Should().BeTrue();
        maybe.Value.Value.Should().Be(99.99m);
        context.ModelState.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task MaybeBinder_NegativeDecimal_AddsValidationError()
    {
        // Arrange
        var binder = new MaybeModelBinder<Price, decimal>();
        var context = CreateBindingContext("price", "-10.00");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        context.ModelState["price"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("Price cannot be negative.");
    }

    #endregion

    #region MaybeModelBinder — Null Binding Context

    [Fact]
    public async Task MaybeBinder_NullBindingContext_ThrowsArgumentNullException()
    {
        // Arrange
        var binder = new MaybeModelBinder<ProductCode, string>();

        // Act
        var act = () => binder.BindModelAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region ScalarValueModelBinderProvider — Maybe Types

    [Fact]
    public void Provider_MaybeStringValueObject_ReturnsMaybeBinder()
    {
        // Arrange
        var provider = new ScalarValueModelBinderProvider();
        var context = CreateBinderProviderContext(typeof(Maybe<ProductCode>));

        // Act
        var binder = provider.GetBinder(context);

        // Assert
        binder.Should().NotBeNull();
        binder.Should().BeOfType<MaybeModelBinder<ProductCode, string>>();
    }

    [Fact]
    public void Provider_MaybeGuidValueObject_ReturnsMaybeBinder()
    {
        // Arrange
        var provider = new ScalarValueModelBinderProvider();
        var context = CreateBinderProviderContext(typeof(Maybe<UserId>));

        // Act
        var binder = provider.GetBinder(context);

        // Assert
        binder.Should().NotBeNull();
        binder.Should().BeOfType<MaybeModelBinder<UserId, Guid>>();
    }

    [Fact]
    public void Provider_MaybeIntValueObject_ReturnsMaybeBinder()
    {
        // Arrange
        var provider = new ScalarValueModelBinderProvider();
        var context = CreateBinderProviderContext(typeof(Maybe<Quantity>));

        // Act
        var binder = provider.GetBinder(context);

        // Assert
        binder.Should().NotBeNull();
        binder.Should().BeOfType<MaybeModelBinder<Quantity, int>>();
    }

    [Fact]
    public void Provider_MaybeDecimalValueObject_ReturnsMaybeBinder()
    {
        // Arrange
        var provider = new ScalarValueModelBinderProvider();
        var context = CreateBinderProviderContext(typeof(Maybe<Price>));

        // Act
        var binder = provider.GetBinder(context);

        // Assert
        binder.Should().NotBeNull();
        binder.Should().BeOfType<MaybeModelBinder<Price, decimal>>();
    }

    [Fact]
    public void Provider_MaybeString_ReturnsNull()
    {
        // Arrange — Maybe<string> is not a scalar value object
        var provider = new ScalarValueModelBinderProvider();
        var context = CreateBinderProviderContext(typeof(Maybe<string>));

        // Act
        var binder = provider.GetBinder(context);

        // Assert
        binder.Should().BeNull("Maybe<string> is not wrapping a scalar value object");
    }

    [Fact]
    public void Provider_DirectValueObject_ReturnsScalarBinder()
    {
        // Arrange — direct value object should still use ScalarValueModelBinder
        var provider = new ScalarValueModelBinderProvider();
        var context = CreateBinderProviderContext(typeof(ProductCode));

        // Act
        var binder = provider.GetBinder(context);

        // Assert
        binder.Should().NotBeNull();
        binder.Should().BeOfType<ScalarValueModelBinder<ProductCode, string>>();
    }

    #endregion

    #region Helper Methods

    private static DefaultModelBindingContext CreateBindingContext(string modelName, string? value)
    {
        var valueProvider = new SimpleValueProvider();
        if (value is not null)
            valueProvider.Add(modelName, value);

        return new DefaultModelBindingContext
        {
            ModelName = modelName,
            ValueProvider = valueProvider,
            ModelState = new ModelStateDictionary()
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
        public override ModelPropertyCollection Properties => new ModelPropertyCollection(Array.Empty<ModelMetadata>());
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

    #endregion
}
