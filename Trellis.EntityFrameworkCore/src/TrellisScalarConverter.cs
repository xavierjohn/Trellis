namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

/// <summary>
/// Value converter for Trellis <see cref="IScalarValue{TSelf, TPrimitive}"/> types.
/// <para>
/// Converts to database using the <c>Value</c> property and from database
/// using the type's validation factory methods. Invalid persisted values throw
/// <see cref="TrellisPersistenceMappingException"/> with explicit materialization details.
/// </para>
/// </summary>
/// <typeparam name="TModel">The Trellis value object type (e.g., <c>EmailAddress</c>).</typeparam>
/// <typeparam name="TProvider">The database provider type (e.g., <c>string</c>).</typeparam>
public class TrellisScalarConverter<TModel, TProvider> : ValueConverter<TModel, TProvider>
    where TModel : class
{
    private static readonly TrellisValueObjectInfo s_valueObject = ResolveValueObject();
    private static readonly Func<TProvider, Result<TModel>>? s_tryCreate =
        s_valueObject.Category == TrellisValueObjectCategory.Scalar ? BuildTryCreateDelegate() : null;
    private static readonly Func<string, Result<TModel>>? s_tryFromName =
        s_valueObject.Category == TrellisValueObjectCategory.Symbolic ? BuildTryFromNameDelegate() : null;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrellisScalarConverter{TModel, TProvider}"/> class.
    /// </summary>
    public TrellisScalarConverter() : base(
        BuildToProviderExpression(),
        BuildToModelExpression())
    {
    }

    private static Expression<Func<TModel, TProvider>> BuildToProviderExpression()
    {
        var param = Expression.Parameter(typeof(TModel), "v");
        var valueProp = typeof(TModel).GetProperty("Value")
            ?? throw new InvalidOperationException(
                $"{typeof(TModel).Name} must have a Value property.");
        var body = Expression.Property(param, valueProp);
        return Expression.Lambda<Func<TModel, TProvider>>(body, param);
    }

    private static Expression<Func<TProvider, TModel>> BuildToModelExpression()
    {
        if (s_valueObject.ProviderType != typeof(TProvider))
            throw new InvalidOperationException(
                $"{typeof(TModel).Name} uses provider type {s_valueObject.ProviderType.Name}, not {typeof(TProvider).Name}.");

        return s_valueObject.Category switch
        {
            TrellisValueObjectCategory.Scalar => BuildScalarToModelExpression(),
            TrellisValueObjectCategory.Symbolic => BuildSymbolicToModelExpression(),
            _ => throw new InvalidOperationException($"Unsupported Trellis value object category '{s_valueObject.Category}'.")
        };
    }

    private static TrellisValueObjectInfo ResolveValueObject() =>
        TrellisTypeScanner.FindValueObject(typeof(TModel))
        ?? throw new InvalidOperationException(
            $"{typeof(TModel).Name} is not a supported Trellis value object.");

    private static Expression<Func<TProvider, TModel>> BuildScalarToModelExpression()
    {
        var param = Expression.Parameter(typeof(TProvider), "v");
        var materializeMethod = typeof(TrellisScalarConverter<TModel, TProvider>)
            .GetMethod(nameof(MaterializeScalar), BindingFlags.NonPublic | BindingFlags.Static)!;
        var body = Expression.Call(materializeMethod, param);
        return Expression.Lambda<Func<TProvider, TModel>>(body, param);
    }

    private static Expression<Func<TProvider, TModel>> BuildSymbolicToModelExpression()
    {
        if (typeof(TProvider) != typeof(string))
            throw new InvalidOperationException(
                $"Symbolic value object {typeof(TModel).Name} must use a string provider type.");

        var param = Expression.Parameter(typeof(TProvider), "v");
        var tryFromNameMethod = typeof(TModel).GetMethod(
                "TryFromName",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                [typeof(string), typeof(string)])
            ?? throw new InvalidOperationException(
                $"{typeof(TModel).Name} must have a static TryFromName(string, string?) method.");

        var materializeMethod = typeof(TrellisScalarConverter<TModel, TProvider>)
            .GetMethod(nameof(MaterializeSymbolic), BindingFlags.NonPublic | BindingFlags.Static)!;
        var body = Expression.Call(materializeMethod, param);
        return Expression.Lambda<Func<TProvider, TModel>>(body, param);
    }

    private static Func<TProvider, Result<TModel>> BuildTryCreateDelegate()
    {
        var param = Expression.Parameter(typeof(TProvider), "v");
        var tryCreateMethod = typeof(TModel).GetMethod(
                "TryCreate",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                [typeof(TProvider), typeof(string)])
            ?? typeof(TModel).GetMethod(
                "TryCreate",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                [typeof(TProvider)])
            ?? throw new InvalidOperationException(
                $"{typeof(TModel).Name} must have a static TryCreate({typeof(TProvider).Name}, string?) method.");

        Expression body = tryCreateMethod.GetParameters().Length == 2
            ? Expression.Call(tryCreateMethod, param, Expression.Constant(null, typeof(string)))
            : Expression.Call(tryCreateMethod, param);

        return Expression.Lambda<Func<TProvider, Result<TModel>>>(body, param).Compile();
    }

    private static Func<string, Result<TModel>> BuildTryFromNameDelegate()
    {
        var param = Expression.Parameter(typeof(string), "v");
        var tryFromNameMethod = typeof(TModel).GetMethod(
                "TryFromName",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                [typeof(string), typeof(string)])
            ?? throw new InvalidOperationException(
                $"{typeof(TModel).Name} must have a static TryFromName(string, string?) method.");

        var body = Expression.Call(tryFromNameMethod, param, Expression.Constant(null, typeof(string)));
        return Expression.Lambda<Func<string, Result<TModel>>>(body, param).Compile();
    }

    private static TModel MaterializeScalar(TProvider value)
    {
        try
        {
            var result = s_tryCreate!(value);
            return MaterializeResult(result, value, "TryCreate");
        }
        catch (TrellisPersistenceMappingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TrellisPersistenceMappingException(
                typeof(TModel),
                value,
                "TryCreate",
                "The factory threw an exception before returning a validation result.",
                ex);
        }
    }

    private static TModel MaterializeSymbolic(TProvider value)
    {
        try
        {
            var result = s_tryFromName!((string)(object)value!);
            return MaterializeResult(result, value, "TryFromName");
        }
        catch (TrellisPersistenceMappingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TrellisPersistenceMappingException(
                typeof(TModel),
                value,
                "TryFromName",
                "The factory threw an exception before returning a validation result.",
                ex);
        }
    }

    private static TModel MaterializeResult(Result<TModel> result, object? persistedValue, string factoryMethod)
    {
        if (result.IsFailure)
            throw new TrellisPersistenceMappingException(typeof(TModel), persistedValue, factoryMethod, result.Error.Detail);

        return result.Value;
    }
}