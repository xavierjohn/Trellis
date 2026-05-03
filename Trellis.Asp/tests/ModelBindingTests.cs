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
/// Tests for model binding of scalar value objects from route/query/form/headers.
/// </summary>
public class ModelBindingTests
{
    #region Test Value Objects

    public class UserId : ScalarValueObject<UserId, Guid>, IScalarValue<UserId, Guid>
    {
        private UserId(Guid value) : base(value) { }

        public static Result<UserId> TryCreate(Guid value, string? fieldName = null)
        {
            var field = fieldName ?? "userId";
            if (value == Guid.Empty)
                return Result.Fail<Asp.Tests.ModelBindingTests.UserId>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "UserId cannot be empty." })));
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
                return Result.Fail<Asp.Tests.ModelBindingTests.ProductCode>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "ProductCode is required." })));
            if (value.Length < 3)
                return Result.Fail<Asp.Tests.ModelBindingTests.ProductCode>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "ProductCode must be at least 3 characters." })));
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
                return Result.Fail<Asp.Tests.ModelBindingTests.Quantity>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Quantity must be greater than zero." })));
            if (value > 1000)
                return Result.Fail<Asp.Tests.ModelBindingTests.Quantity>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Quantity cannot exceed 1000." })));
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
                return Result.Fail<Asp.Tests.ModelBindingTests.Price>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Price cannot be negative." })));
            return Result.Ok(new Price(value));
        }

        public static Result<Price> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    #endregion

    #region ScalarValueModelBinder Tests

    [Fact]
    public async Task ModelBinder_ValidGuid_BindsSuccessfully()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var binder = new ScalarValueModelBinder<UserId, Guid>();
        var context = CreateBindingContext("userId", guid.ToString());

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeTrue();
        var userId = context.Result.Model as UserId;
        userId.Should().NotBeNull();
        userId!.Value.Should().Be(guid);
        context.ModelState.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task ModelBinder_EmptyGuid_AddsValidationError()
    {
        // Arrange
        var binder = new ScalarValueModelBinder<UserId, Guid>();
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
    public async Task ModelBinder_ValidString_BindsSuccessfully()
    {
        // Arrange
        var binder = new ScalarValueModelBinder<ProductCode, string>();
        var context = CreateBindingContext("productCode", "ABC123");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeTrue();
        var productCode = context.Result.Model as ProductCode;
        productCode.Should().NotBeNull();
        productCode!.Value.Should().Be("ABC123");
        context.ModelState.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task ModelBinder_StringTooShort_AddsValidationError()
    {
        // Arrange
        var binder = new ScalarValueModelBinder<ProductCode, string>();
        var context = CreateBindingContext("productCode", "AB");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        context.ModelState["productCode"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("ProductCode must be at least 3 characters.");
    }

    [Fact]
    public async Task ModelBinder_EmptyString_AddsConversionError()
    {
        // After m-14: PrimitiveConverter no longer short-circuits empty strings for
        // string-typed value objects. The error now flows from ProductCode.TryCreate,
        // which is the right place for the empty-vs-required decision.
        var binder = new ScalarValueModelBinder<ProductCode, string>();
        var context = CreateBindingContext("productCode", "");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        context.ModelState["productCode"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("ProductCode is required.");
    }

    [Fact]
    public async Task ModelBinder_ValidInt_BindsSuccessfully()
    {
        // Arrange
        var binder = new ScalarValueModelBinder<Quantity, int>();
        var context = CreateBindingContext("quantity", "42");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeTrue();
        var quantity = context.Result.Model as Quantity;
        quantity.Should().NotBeNull();
        quantity!.Value.Should().Be(42);
        context.ModelState.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task ModelBinder_IntBelowMinimum_AddsValidationError()
    {
        // Arrange
        var binder = new ScalarValueModelBinder<Quantity, int>();
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
    public async Task ModelBinder_IntAboveMaximum_AddsValidationError()
    {
        // Arrange
        var binder = new ScalarValueModelBinder<Quantity, int>();
        var context = CreateBindingContext("quantity", "1001");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        context.ModelState["quantity"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("Quantity cannot exceed 1000.");
    }

    [Fact]
    public async Task ModelBinder_InvalidInt_ReturnsParseError()
    {
        // Arrange
        var binder = new ScalarValueModelBinder<Quantity, int>();
        var context = CreateBindingContext("quantity", "not-a-number");

        // Act
        await binder.BindModelAsync(context);

        // Assert - invalid strings return a parsing error before TryCreate is called
        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        context.ModelState["quantity"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("is not a valid integer");
    }

    [Fact]
    public async Task ModelBinder_ValidDecimal_BindsSuccessfully()
    {
        // Arrange
        var binder = new ScalarValueModelBinder<Price, decimal>();
        var context = CreateBindingContext("price", "99.99");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeTrue();
        var price = context.Result.Model as Price;
        price.Should().NotBeNull();
        price!.Value.Should().Be(99.99m);
        context.ModelState.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task ModelBinder_NegativeDecimal_AddsValidationError()
    {
        // Arrange
        var binder = new ScalarValueModelBinder<Price, decimal>();
        var context = CreateBindingContext("price", "-10.00");

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        context.ModelState["price"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("Price cannot be negative.");
    }

    [Fact]
    public async Task ModelBinder_NoValue_DoesNotBind()
    {
        // Arrange
        var binder = new ScalarValueModelBinder<ProductCode, string>();
        var context = CreateBindingContext("productCode", null);

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.ErrorCount.Should().Be(0); // No value means optional
    }

    [Fact]
    public async Task ModelBinder_InvalidGuid_ReturnsParseError()
    {
        // Arrange
        var binder = new ScalarValueModelBinder<UserId, Guid>();
        var context = CreateBindingContext("userId", "not-a-guid");

        // Act
        await binder.BindModelAsync(context);

        // Assert - invalid strings return a parsing error before TryCreate is called
        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        context.ModelState["userId"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("is not a valid GUID");
    }

    [Fact]
    public async Task ModelBinder_UsesFieldNameInValidationErrors()
    {
        // Arrange
        var binder = new ScalarValueModelBinder<Quantity, int>();
        var context = CreateBindingContext("orderQuantity", "0"); // Using custom field name

        // Act
        await binder.BindModelAsync(context);

        // Assert
        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        // Field name from context should be used in the error
        context.ModelState["orderQuantity"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("Quantity must be greater than zero.");
    }

    #endregion

    #region ScalarValueModelBinderProvider Tests

    [Fact]
    public void ModelBinderProvider_ScalarValueObjectType_ReturnsBinder()
    {
        // Arrange
        var provider = new ScalarValueModelBinderProvider();
        var context = CreateBinderProviderContext(typeof(UserId));

        // Act
        var binder = provider.GetBinder(context);

        // Assert
        binder.Should().NotBeNull();
        binder.Should().BeOfType<ScalarValueModelBinder<UserId, Guid>>();
    }

    [Fact]
    public void ModelBinderProvider_NonValueObjectType_ReturnsNull()
    {
        // Arrange
        var provider = new ScalarValueModelBinderProvider();
        var context = CreateBinderProviderContext(typeof(string));

        // Act
        var binder = provider.GetBinder(context);

        // Assert
        binder.Should().BeNull();
    }

    [Fact]
    public void ModelBinderProvider_StringValueObject_ReturnsBinder()
    {
        // Arrange
        var provider = new ScalarValueModelBinderProvider();
        var context = CreateBinderProviderContext(typeof(ProductCode));

        // Act
        var binder = provider.GetBinder(context);

        // Assert
        binder.Should().NotBeNull();
        binder.Should().BeOfType<ScalarValueModelBinder<ProductCode, string>>();
    }

    [Fact]
    public void ModelBinderProvider_IntValueObject_ReturnsBinder()
    {
        // Arrange
        var provider = new ScalarValueModelBinderProvider();
        var context = CreateBinderProviderContext(typeof(Quantity));

        // Act
        var binder = provider.GetBinder(context);

        // Assert
        binder.Should().NotBeNull();
        binder.Should().BeOfType<ScalarValueModelBinder<Quantity, int>>();
    }

    [Fact]
    public void ModelBinderProvider_DecimalValueObject_ReturnsBinder()
    {
        // Arrange
        var provider = new ScalarValueModelBinderProvider();
        var context = CreateBinderProviderContext(typeof(Price));

        // Act
        var binder = provider.GetBinder(context);

        // Assert
        binder.Should().NotBeNull();
        binder.Should().BeOfType<ScalarValueModelBinder<Price, decimal>>();
    }

    [Fact]
    public void ModelBinderProvider_NullContext_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = new ScalarValueModelBinderProvider();

        // Act
        var act = () => provider.GetBinder(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Helper Methods

    private static DefaultModelBindingContext CreateBindingContext(string modelName, string? value)
    {
        var valueProvider = new SimpleValueProvider();
        if (value is not null)
        {
            valueProvider.Add(modelName, value);
        }

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