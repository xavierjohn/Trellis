namespace Trellis.Asp.ModelBinding;

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Trellis;

/// <summary>
/// Model binder for scalar value types.
/// Validates scalar values during model binding by calling TryCreate.
/// </summary>
/// <typeparam name="TValue">The scalar value type.</typeparam>
/// <typeparam name="TPrimitive">The underlying primitive type.</typeparam>
/// <remarks>
/// <para>
/// This binder is automatically used for any type that implements 
/// <see cref="IScalarValue{TSelf, TPrimitive}"/>. It intercepts model binding
/// and calls the static <c>TryCreate</c> method to create validated scalar values.
/// </para>
/// <para>
/// Validation errors are added to <see cref="ModelStateDictionary"/>, which integrates
/// with ASP.NET Core's standard validation infrastructure. When used with
/// <c>[ApiController]</c>, invalid requests automatically return 400 Bad Request.
/// </para>
/// </remarks>
public class ScalarValueModelBinder<TValue, TPrimitive> : ScalarValueModelBinderBase<TValue, TValue, TPrimitive>
    where TValue : IScalarValue<TValue, TPrimitive>
    where TPrimitive : IComparable
{
    /// <inheritdoc />
    protected override ModelBindingResult OnMissingValue() => default;

    /// <inheritdoc />
    protected override ModelBindingResult OnSuccess(TValue value) =>
        ModelBindingResult.Success(value);
}