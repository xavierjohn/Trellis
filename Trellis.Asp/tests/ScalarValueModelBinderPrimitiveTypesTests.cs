namespace Trellis.Asp.Tests;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Trellis;
using Trellis.Asp.ModelBinding;
using Xunit;

/// <summary>
/// Tests for ScalarValueModelBinder covering all primitive type conversions.
/// These tests ensure ConvertToPrimitive handles all supported types correctly.
/// </summary>
public class ScalarValueModelBinderPrimitiveTypesTests
{
    #region Value Object Types for Each Primitive

    public sealed class StringVO : ScalarValueObject<StringVO, string>, IScalarValue<StringVO, string>
    {
        private StringVO(string value) : base(value) { }
        public static Result<StringVO> TryCreate(string? value, string? fieldName = null) =>
            string.IsNullOrWhiteSpace(value)
                ? Error.Validation("Required", fieldName ?? "value")
                : new StringVO(value);
    }

    public sealed class GuidVO : ScalarValueObject<GuidVO, Guid>, IScalarValue<GuidVO, Guid>
    {
        private GuidVO(Guid value) : base(value) { }
        public static Result<GuidVO> TryCreate(Guid value, string? fieldName = null) =>
            value == Guid.Empty
                ? Error.Validation("Cannot be empty", fieldName ?? "value")
                : new GuidVO(value);
    }

    public sealed class NonNegativeIntVO : ScalarValueObject<NonNegativeIntVO, int>, IScalarValue<NonNegativeIntVO, int>
    {
        private NonNegativeIntVO(int value) : base(value) { }
        public static Result<NonNegativeIntVO> TryCreate(int value, string? fieldName = null) =>
            value < 0
                ? Error.Validation("Must be non-negative", fieldName ?? "value")
                : new NonNegativeIntVO(value);
    }

    public sealed class LongVO : ScalarValueObject<LongVO, long>, IScalarValue<LongVO, long>
    {
        private LongVO(long value) : base(value) { }
        public static Result<LongVO> TryCreate(long value, string? fieldName = null) =>
            value < 0
                ? Error.Validation("Must be non-negative", fieldName ?? "value")
                : new LongVO(value);
    }

    public sealed class DecimalVO : ScalarValueObject<DecimalVO, decimal>, IScalarValue<DecimalVO, decimal>
    {
        private DecimalVO(decimal value) : base(value) { }
        public static Result<DecimalVO> TryCreate(decimal value, string? fieldName = null) =>
            value < 0
                ? Error.Validation("Must be non-negative", fieldName ?? "value")
                : new DecimalVO(value);
    }

    public sealed class DoubleVO : ScalarValueObject<DoubleVO, double>, IScalarValue<DoubleVO, double>
    {
        private DoubleVO(double value) : base(value) { }
        public static Result<DoubleVO> TryCreate(double value, string? fieldName = null) =>
            value < 0
                ? Error.Validation("Must be non-negative", fieldName ?? "value")
                : new DoubleVO(value);
    }

    public sealed class BoolVO : ScalarValueObject<BoolVO, bool>, IScalarValue<BoolVO, bool>
    {
        private BoolVO(bool value) : base(value) { }
        public static Result<BoolVO> TryCreate(bool value, string? fieldName = null) =>
            new BoolVO(value);
    }

    public sealed class DateTimeVO : ScalarValueObject<DateTimeVO, DateTime>, IScalarValue<DateTimeVO, DateTime>
    {
        private DateTimeVO(DateTime value) : base(value) { }
        public static Result<DateTimeVO> TryCreate(DateTime value, string? fieldName = null) =>
            value == default
                ? Error.Validation("Required", fieldName ?? "value")
                : new DateTimeVO(value);
    }

    public sealed class DateOnlyVO : ScalarValueObject<DateOnlyVO, DateOnly>, IScalarValue<DateOnlyVO, DateOnly>
    {
        private DateOnlyVO(DateOnly value) : base(value) { }
        public static Result<DateOnlyVO> TryCreate(DateOnly value, string? fieldName = null) =>
            value == default
                ? Error.Validation("Required", fieldName ?? "value")
                : new DateOnlyVO(value);
    }

    public sealed class TimeOnlyVO : ScalarValueObject<TimeOnlyVO, TimeOnly>, IScalarValue<TimeOnlyVO, TimeOnly>
    {
        private TimeOnlyVO(TimeOnly value) : base(value) { }
        public static Result<TimeOnlyVO> TryCreate(TimeOnly value, string? fieldName = null) =>
            new TimeOnlyVO(value);
    }

    public sealed class DateTimeOffsetVO : ScalarValueObject<DateTimeOffsetVO, DateTimeOffset>, IScalarValue<DateTimeOffsetVO, DateTimeOffset>
    {
        private DateTimeOffsetVO(DateTimeOffset value) : base(value) { }
        public static Result<DateTimeOffsetVO> TryCreate(DateTimeOffset value, string? fieldName = null) =>
            value == default
                ? Error.Validation("Required", fieldName ?? "value")
                : new DateTimeOffsetVO(value);
    }

    public sealed class ShortVO : ScalarValueObject<ShortVO, short>, IScalarValue<ShortVO, short>
    {
        private ShortVO(short value) : base(value) { }
        public static Result<ShortVO> TryCreate(short value, string? fieldName = null) =>
            value < 0
                ? Error.Validation("Must be non-negative", fieldName ?? "value")
                : new ShortVO(value);
    }

    public sealed class ByteVO : ScalarValueObject<ByteVO, byte>, IScalarValue<ByteVO, byte>
    {
        private ByteVO(byte value) : base(value) { }
        public static Result<ByteVO> TryCreate(byte value, string? fieldName = null) =>
            new ByteVO(value);
    }

    public sealed class FloatVO : ScalarValueObject<FloatVO, float>, IScalarValue<FloatVO, float>
    {
        private FloatVO(float value) : base(value) { }
        public static Result<FloatVO> TryCreate(float value, string? fieldName = null) =>
            value < 0
                ? Error.Validation("Must be non-negative", fieldName ?? "value")
                : new FloatVO(value);
    }

    public enum ProcessingMode
    {
        Unknown = 0,
        Fast = 1,
        Safe = 2,
    }

    public sealed class ProcessingModeVO : ScalarValueObject<ProcessingModeVO, ProcessingMode>, IScalarValue<ProcessingModeVO, ProcessingMode>
    {
        private ProcessingModeVO(ProcessingMode value) : base(value) { }
        public static Result<ProcessingModeVO> TryCreate(ProcessingMode value, string? fieldName = null) =>
            value == ProcessingMode.Unknown
                ? Error.Validation("Mode is required", fieldName ?? "value")
                : new ProcessingModeVO(value);
    }

    #endregion

    #region String Tests

    [Fact]
    public async Task BindModelAsync_String_ValidValue_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<StringVO, string>();
        var context = CreateBindingContext<StringVO>("test", "hello world");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        context.Result.Model.Should().BeOfType<StringVO>();
        ((StringVO)context.Result.Model!).Value.Should().Be("hello world");
    }

    [Fact]
    public async Task BindModelAsync_String_EmptyValue_ReturnsValidationError()
    {
        var binder = new ScalarValueModelBinder<StringVO, string>();
        var context = CreateBindingContext<StringVO>("test", "");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    #endregion

    #region Guid Tests

    [Fact]
    public async Task BindModelAsync_Guid_ValidValue_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<GuidVO, Guid>();
        var guid = Guid.NewGuid();
        var context = CreateBindingContext<GuidVO>("id", guid.ToString());

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        ((GuidVO)context.Result.Model!).Value.Should().Be(guid);
    }

    [Fact]
    public async Task BindModelAsync_Guid_InvalidFormat_ReturnsError()
    {
        var binder = new ScalarValueModelBinder<GuidVO, Guid>();
        var context = CreateBindingContext<GuidVO>("id", "not-a-guid");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
        context.ModelState["id"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("is not a valid GUID");
    }

    [Fact]
    public async Task BindModelAsync_Guid_EmptyGuid_ReturnsValidationError()
    {
        var binder = new ScalarValueModelBinder<GuidVO, Guid>();
        var context = CreateBindingContext<GuidVO>("id", Guid.Empty.ToString());

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    #endregion

    #region Int Tests

    [Fact]
    public async Task BindModelAsync_Int_ValidValue_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<NonNegativeIntVO, int>();
        var context = CreateBindingContext<NonNegativeIntVO>("count", "42");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        ((NonNegativeIntVO)context.Result.Model!).Value.Should().Be(42);
    }

    [Fact]
    public async Task BindModelAsync_Int_NegativeValue_ReturnsValidationError()
    {
        var binder = new ScalarValueModelBinder<NonNegativeIntVO, int>();
        var context = CreateBindingContext<NonNegativeIntVO>("count", "-5");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task BindModelAsync_Int_InvalidFormat_ReturnsError()
    {
        var binder = new ScalarValueModelBinder<NonNegativeIntVO, int>();
        var context = CreateBindingContext<NonNegativeIntVO>("count", "abc");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState["count"]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("is not a valid integer");
    }

    [Fact]
    public async Task BindModelAsync_Int_MaxValue_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<NonNegativeIntVO, int>();
        var context = CreateBindingContext<NonNegativeIntVO>("count", int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        ((NonNegativeIntVO)context.Result.Model!).Value.Should().Be(int.MaxValue);
    }

    #endregion

    #region Long Tests

    [Fact]
    public async Task BindModelAsync_Long_ValidValue_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<LongVO, long>();
        var context = CreateBindingContext<LongVO>("id", "9223372036854775807");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        ((LongVO)context.Result.Model!).Value.Should().Be(long.MaxValue);
    }

    [Fact]
    public async Task BindModelAsync_Long_InvalidFormat_ReturnsError()
    {
        var binder = new ScalarValueModelBinder<LongVO, long>();
        var context = CreateBindingContext<LongVO>("id", "not-a-number");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    #endregion

    #region Decimal Tests

    [Fact]
    public async Task BindModelAsync_Decimal_ValidValue_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<DecimalVO, decimal>();
        var context = CreateBindingContext<DecimalVO>("price", "123.45");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        ((DecimalVO)context.Result.Model!).Value.Should().Be(123.45m);
    }

    [Fact]
    public async Task BindModelAsync_Decimal_NegativeValue_ReturnsValidationError()
    {
        var binder = new ScalarValueModelBinder<DecimalVO, decimal>();
        var context = CreateBindingContext<DecimalVO>("price", "-99.99");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    #endregion

    #region Double Tests

    [Fact]
    public async Task BindModelAsync_Double_ValidValue_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<DoubleVO, double>();
        var context = CreateBindingContext<DoubleVO>("rate", "3.14159");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        ((DoubleVO)context.Result.Model!).Value.Should().BeApproximately(3.14159, 0.00001);
    }

    [Fact]
    public async Task BindModelAsync_Double_InvalidFormat_ReturnsError()
    {
        var binder = new ScalarValueModelBinder<DoubleVO, double>();
        var context = CreateBindingContext<DoubleVO>("rate", "not-a-double");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    #endregion

    #region Bool Tests

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("FALSE", false)]
    public async Task BindModelAsync_Bool_ValidValues_BindsSuccessfully(string input, bool expected)
    {
        var binder = new ScalarValueModelBinder<BoolVO, bool>();
        var context = CreateBindingContext<BoolVO>("flag", input);

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        ((BoolVO)context.Result.Model!).Value.Should().Be(expected);
    }

    [Fact]
    public async Task BindModelAsync_Bool_InvalidFormat_ReturnsError()
    {
        var binder = new ScalarValueModelBinder<BoolVO, bool>();
        var context = CreateBindingContext<BoolVO>("flag", "yes");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    #endregion

    #region DateTime Tests

    [Fact]
    public async Task BindModelAsync_DateTime_ValidValue_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<DateTimeVO, DateTime>();
        var context = CreateBindingContext<DateTimeVO>("date", "2024-06-15T10:30:00");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        var result = (DateTimeVO)context.Result.Model!;
        result.Value.Year.Should().Be(2024);
        result.Value.Month.Should().Be(6);
        result.Value.Day.Should().Be(15);
    }

    [Fact]
    public async Task BindModelAsync_DateTime_InvalidFormat_ReturnsError()
    {
        var binder = new ScalarValueModelBinder<DateTimeVO, DateTime>();
        var context = CreateBindingContext<DateTimeVO>("date", "not-a-date");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    #endregion

    #region DateOnly Tests

    [Fact]
    public async Task BindModelAsync_DateOnly_ValidValue_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<DateOnlyVO, DateOnly>();
        var context = CreateBindingContext<DateOnlyVO>("birthDate", "2024-06-15");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        var result = (DateOnlyVO)context.Result.Model!;
        result.Value.Should().Be(new DateOnly(2024, 6, 15));
    }

    [Fact]
    public async Task BindModelAsync_DateOnly_InvalidFormat_ReturnsError()
    {
        var binder = new ScalarValueModelBinder<DateOnlyVO, DateOnly>();
        var context = CreateBindingContext<DateOnlyVO>("birthDate", "invalid");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    #endregion

    #region TimeOnly Tests

    [Fact]
    public async Task BindModelAsync_TimeOnly_ValidValue_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<TimeOnlyVO, TimeOnly>();
        var context = CreateBindingContext<TimeOnlyVO>("startTime", "14:30:00");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        var result = (TimeOnlyVO)context.Result.Model!;
        result.Value.Should().Be(new TimeOnly(14, 30, 0));
    }

    [Fact]
    public async Task BindModelAsync_TimeOnly_InvalidFormat_ReturnsError()
    {
        var binder = new ScalarValueModelBinder<TimeOnlyVO, TimeOnly>();
        var context = CreateBindingContext<TimeOnlyVO>("startTime", "25:00:00");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    #endregion

    #region DateTimeOffset Tests

    [Fact]
    public async Task BindModelAsync_DateTimeOffset_ValidValue_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<DateTimeOffsetVO, DateTimeOffset>();
        var context = CreateBindingContext<DateTimeOffsetVO>("timestamp", "2024-06-15T10:30:00+02:00");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        var result = (DateTimeOffsetVO)context.Result.Model!;
        result.Value.Year.Should().Be(2024);
        result.Value.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task BindModelAsync_DateTimeOffset_InvalidFormat_ReturnsError()
    {
        var binder = new ScalarValueModelBinder<DateTimeOffsetVO, DateTimeOffset>();
        var context = CreateBindingContext<DateTimeOffsetVO>("timestamp", "invalid");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    #endregion

    #region Short Tests

    [Fact]
    public async Task BindModelAsync_Short_ValidValue_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<ShortVO, short>();
        var context = CreateBindingContext<ShortVO>("code", "32767");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        ((ShortVO)context.Result.Model!).Value.Should().Be(short.MaxValue);
    }

    [Fact]
    public async Task BindModelAsync_Short_Overflow_ReturnsError()
    {
        var binder = new ScalarValueModelBinder<ShortVO, short>();
        var context = CreateBindingContext<ShortVO>("code", "99999");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    #endregion

    #region Byte Tests

    [Fact]
    public async Task BindModelAsync_Byte_ValidValue_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<ByteVO, byte>();
        var context = CreateBindingContext<ByteVO>("level", "255");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        ((ByteVO)context.Result.Model!).Value.Should().Be(byte.MaxValue);
    }

    [Fact]
    public async Task BindModelAsync_Byte_Overflow_ReturnsError()
    {
        var binder = new ScalarValueModelBinder<ByteVO, byte>();
        var context = CreateBindingContext<ByteVO>("level", "256");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    #endregion

    #region Float Tests

    [Fact]
    public async Task BindModelAsync_Float_ValidValue_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<FloatVO, float>();
        var context = CreateBindingContext<FloatVO>("ratio", "0.5");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        ((FloatVO)context.Result.Model!).Value.Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public async Task BindModelAsync_Float_InvalidFormat_ReturnsError()
    {
        var binder = new ScalarValueModelBinder<FloatVO, float>();
        var context = CreateBindingContext<FloatVO>("ratio", "not-a-float");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task BindModelAsync_Enum_ValidName_BindsSuccessfully()
    {
        var binder = new ScalarValueModelBinder<ProcessingModeVO, ProcessingMode>();
        var context = CreateBindingContext<ProcessingModeVO>("mode", "Safe");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        ((ProcessingModeVO)context.Result.Model!).Value.Should().Be(ProcessingMode.Safe);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task BindModelAsync_MissingValue_DoesNotSetModel()
    {
        var binder = new ScalarValueModelBinder<StringVO, string>();
        var context = CreateBindingContextWithNoValue<StringVO>("test");

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
        context.ModelState.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task BindModelAsync_NullContext_ThrowsArgumentNullException()
    {
        var binder = new ScalarValueModelBinder<StringVO, string>();

        var act = () => binder.BindModelAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region Helper Methods

    private static DefaultModelBindingContext CreateBindingContext<TModel>(string modelName, string value)
    {
        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        var actionContext = new ActionContext(httpContext, routeData, new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());

        var valueProvider = new QueryStringValueProvider(
            BindingSource.Query,
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                { modelName, value }
            }),
            System.Globalization.CultureInfo.InvariantCulture);

        var bindingContext = new DefaultModelBindingContext
        {
            ActionContext = actionContext,
            ModelName = modelName,
            ModelState = new ModelStateDictionary(),
            ModelMetadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(TModel)),
            ValueProvider = valueProvider
        };

        return bindingContext;
    }

    private static DefaultModelBindingContext CreateBindingContextWithNoValue<TModel>(string modelName)
    {
        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        var actionContext = new ActionContext(httpContext, routeData, new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());

        var valueProvider = new QueryStringValueProvider(
            BindingSource.Query,
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()),
            System.Globalization.CultureInfo.InvariantCulture);

        var bindingContext = new DefaultModelBindingContext
        {
            ActionContext = actionContext,
            ModelName = modelName,
            ModelState = new ModelStateDictionary(),
            ModelMetadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(TModel)),
            ValueProvider = valueProvider
        };

        return bindingContext;
    }

    #endregion
}