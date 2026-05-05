namespace Trellis.Asp.ModelBinding;

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Trellis;

/// <summary>
/// Base model binder for scalar value types that handles primitive conversion,
/// validation via <c>TryCreate</c>, and error collection.
/// </summary>
/// <typeparam name="TResult">The result type of binding (e.g., <c>TValue</c> or <c>Maybe&lt;TValue&gt;</c>).</typeparam>
/// <typeparam name="TValue">The scalar value object type.</typeparam>
/// <typeparam name="TPrimitive">The underlying primitive type.</typeparam>
public abstract class ScalarValueModelBinderBase<TResult, TValue, TPrimitive> : IModelBinder
    where TValue : IScalarValue<TValue, TPrimitive>
    where TPrimitive : IComparable
{
    /// <summary>
    /// Returns the binding result when no value is provided.
    /// </summary>
    protected abstract ModelBindingResult OnMissingValue();

    /// <summary>
    /// Returns the binding result when an empty string is provided.
    /// Returns <c>null</c> to continue with normal conversion logic.
    /// </summary>
    protected virtual ModelBindingResult? OnEmptyValue() => null;

    /// <summary>
    /// Wraps a successfully validated value object into the binding result.
    /// </summary>
    protected abstract ModelBindingResult OnSuccess(TValue value);

    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var modelName = bindingContext.ModelName;
        var valueProviderResult = bindingContext.ValueProvider.GetValue(modelName);

        if (valueProviderResult == ValueProviderResult.None)
        {
            bindingContext.Result = OnMissingValue();
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(modelName, valueProviderResult);

        var rawValue = valueProviderResult.FirstValue;

        var emptyResult = OnEmptyValue();
        if (emptyResult is not null && string.IsNullOrEmpty(rawValue))
        {
            bindingContext.Result = emptyResult.Value;
            return Task.CompletedTask;
        }

        var parseResult = PrimitiveConverter.ConvertToPrimitive<TPrimitive>(rawValue);

        // Combined check: TryGetValue(out value, out error) is mutually exclusive on Result<T>,
        // so a single call replaces the two separate TryGetError/TryGetValue calls (and the
        // accompanying dead defensive throw the old shape required after them).
        if (!parseResult.TryGetValue(out var primitiveValue, out var parseError))
        {
            bindingContext.ModelState.AddModelError(modelName, parseError.Detail ?? parseError.Code);
            bindingContext.Result = ModelBindingResult.Failed();
            return Task.CompletedTask;
        }

        var result = TValue.TryCreate(primitiveValue, modelName);

        if (result.TryGetValue(out var typedValue))
        {
            bindingContext.Result = OnSuccess(typedValue);
        }
        else if (result.TryGetError(out var createError))
        {
            bindingContext.ModelState.AddResultErrors(modelName, createError);
            bindingContext.Result = ModelBindingResult.Failed();
        }

        return Task.CompletedTask;
    }
}