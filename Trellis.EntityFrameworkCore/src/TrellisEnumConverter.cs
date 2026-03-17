namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

/// <summary>
/// Value converter for <see cref="RequiredEnum{TSelf}"/> value objects.
/// Stores the enum as its <c>Value</c> (string) in the database
/// and reconstructs it using <c>TryFromName</c>.
/// </summary>
/// <typeparam name="TModel">The concrete RequiredEnum type (e.g., <c>OrderStatus</c>).</typeparam>
public class TrellisEnumConverter<TModel> : ValueConverter<TModel, string>
    where TModel : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrellisEnumConverter{TModel}"/> class.
    /// </summary>
    public TrellisEnumConverter() : base(
        BuildToProviderExpression(),
        BuildToModelExpression())
    {
    }

    private static Expression<Func<TModel, string>> BuildToProviderExpression()
    {
        var param = Expression.Parameter(typeof(TModel), "v");
        var valueProp = typeof(TModel).GetProperty("Value")
            ?? throw new InvalidOperationException(
                $"{typeof(TModel).Name} must have a Value property.");
        var body = Expression.Property(param, valueProp);
        return Expression.Lambda<Func<TModel, string>>(body, param);
    }

    private static Expression<Func<string, TModel>> BuildToModelExpression()
    {
        var param = Expression.Parameter(typeof(string), "v");
        var tryFromNameMethod = typeof(TModel).GetMethod(
                "TryFromName",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                [typeof(string), typeof(string)])
            ?? throw new InvalidOperationException(
                $"{typeof(TModel).Name} must have a static TryFromName(string, string?) method.");

        // TryFromName(v, null) → Result<TModel>.Value
        var call = Expression.Call(tryFromNameMethod, param, Expression.Constant(null, typeof(string)));
        var resultType = typeof(Result<>).MakeGenericType(typeof(TModel));
        var valueProp = resultType.GetProperty("Value")!;
        var body = Expression.Property(call, valueProp);
        return Expression.Lambda<Func<string, TModel>>(body, param);
    }
}