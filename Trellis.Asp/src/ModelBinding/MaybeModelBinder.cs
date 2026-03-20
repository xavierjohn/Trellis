namespace Trellis.Asp.ModelBinding;

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Trellis;

/// <summary>
/// Model binder for <see cref="Maybe{TValue}"/> parameters where <typeparamref name="TValue"/>
/// implements <see cref="IScalarValue{TSelf, TPrimitive}"/>.
/// Binds optional route, query, form, and header parameters to <see cref="Maybe{TValue}"/>.
/// </summary>
/// <typeparam name="TValue">The scalar value type.</typeparam>
/// <typeparam name="TPrimitive">The underlying primitive type.</typeparam>
/// <remarks>
/// <para>
/// Unlike <see cref="ScalarValueModelBinder{TValue, TPrimitive}"/> which leaves absent parameters
/// unbound (relying on ASP.NET Core's default handling), this binder explicitly produces:
/// </para>
/// <list type="bullet">
/// <item>Parameter absent → <c>Maybe.None&lt;TValue&gt;()</c> — success, value not provided</item>
/// <item>Parameter present, valid → <c>Maybe.From(validatedValue)</c> — success with value</item>
/// <item>Parameter present, invalid → validation error in ModelState</item>
/// </list>
/// </remarks>
public class MaybeModelBinder<TValue, TPrimitive> : ScalarValueModelBinderBase<Maybe<TValue>, TValue, TPrimitive>
    where TValue : IScalarValue<TValue, TPrimitive>
    where TPrimitive : IComparable
{
    /// <inheritdoc />
    protected override ModelBindingResult OnMissingValue() =>
        ModelBindingResult.Success(Maybe<TValue>.None);

    /// <inheritdoc />
    protected override ModelBindingResult? OnEmptyValue() =>
        ModelBindingResult.Success(Maybe<TValue>.None);

    /// <inheritdoc />
    protected override ModelBindingResult OnSuccess(TValue value) =>
        ModelBindingResult.Success(Maybe.From(value));
}