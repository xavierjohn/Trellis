namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;
using Trellis.Primitives;

/// <summary>
/// Builds provider-translatable expressions for geographic bounding and spherical distance queries.
/// </summary>
/// <remarks>
/// The configured provider must translate the resulting arithmetic and <see cref="Math"/> calls.
/// Translation failures propagate; these helpers never compile an expression or fall back to client evaluation.
/// </remarks>
public static class GeoCoordinateExpressions
{
    private const double DegreesToRadians = Math.PI / 180;

    private static readonly MethodInfo SinMethod = GetMathMethod(nameof(Math.Sin));
    private static readonly MethodInfo CosMethod = GetMathMethod(nameof(Math.Cos));
    private static readonly MethodInfo SqrtMethod = GetMathMethod(nameof(Math.Sqrt));
    private static readonly MethodInfo AsinMethod = GetMathMethod(nameof(Math.Asin));

    /// <summary>Builds a spherical haversine-distance expression in meters.</summary>
    /// <typeparam name="TEntity">The query entity or projection type.</typeparam>
    /// <param name="latitudeSelector">Selects a latitude in decimal degrees.</param>
    /// <param name="longitudeSelector">Selects a longitude in decimal degrees.</param>
    /// <param name="origin">The fixed query origin.</param>
    /// <returns>
    /// An expression suitable for projection, ordering, filtering, or a computed
    /// <see cref="SeekDefinition{T,TState}"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">A selector or <paramref name="origin"/> is null.</exception>
    /// <remarks>
    /// Uses the same mean Earth radius and spherical model as
    /// <see cref="GeoCoordinate.DistanceMetersTo"/>. The database provider determines the
    /// final floating-point precision.
    /// </remarks>
    public static Expression<Func<TEntity, double>> DistanceMetersTo<TEntity>(
        Expression<Func<TEntity, double>> latitudeSelector,
        Expression<Func<TEntity, double>> longitudeSelector,
        GeoCoordinate origin)
        where TEntity : class
    {
        var (parameter, latitude, longitude) = PrepareSelectors(latitudeSelector, longitudeSelector);
        ArgumentNullException.ThrowIfNull(origin);
        return Expression.Lambda<Func<TEntity, double>>(
            BuildDistance(latitude, longitude, origin),
            parameter);
    }

    /// <summary>Builds the conservative one-or-two-box prefilter represented by <paramref name="bounds"/>.</summary>
    /// <typeparam name="TEntity">The query entity or projection type.</typeparam>
    /// <param name="latitudeSelector">Selects a latitude in decimal degrees.</param>
    /// <param name="longitudeSelector">Selects a longitude in decimal degrees.</param>
    /// <param name="bounds">Validated spherical search bounds.</param>
    /// <returns>An inclusive predicate over the stored latitude and longitude components.</returns>
    /// <exception cref="ArgumentNullException">A selector or <paramref name="bounds"/> is null.</exception>
    /// <remarks>
    /// This predicate can include false positives. Use <see cref="WithinRadius{TEntity}"/> when
    /// exact spherical-radius membership is required.
    /// </remarks>
    public static Expression<Func<TEntity, bool>> WithinBounds<TEntity>(
        Expression<Func<TEntity, double>> latitudeSelector,
        Expression<Func<TEntity, double>> longitudeSelector,
        GeoBounds bounds)
        where TEntity : class
    {
        var (parameter, latitude, longitude) = PrepareSelectors(latitudeSelector, longitudeSelector);
        ArgumentNullException.ThrowIfNull(bounds);
        return Expression.Lambda<Func<TEntity, bool>>(
            BuildBounds(latitude, longitude, bounds),
            parameter);
    }

    /// <summary>Builds a conservative box prefilter followed by an exact spherical-distance predicate.</summary>
    /// <typeparam name="TEntity">The query entity or projection type.</typeparam>
    /// <param name="latitudeSelector">Selects a latitude in decimal degrees.</param>
    /// <param name="longitudeSelector">Selects a longitude in decimal degrees.</param>
    /// <param name="bounds">Validated center, radius, and conservative boxes.</param>
    /// <returns>An expression matching rows whose computed distance is at most the configured radius.</returns>
    /// <exception cref="ArgumentNullException">A selector or <paramref name="bounds"/> is null.</exception>
    /// <remarks>
    /// The provider evaluates the inclusive distance comparison. Its transcendental functions can round
    /// differently from <see cref="GeoCoordinate.DistanceMetersTo"/> at an exactly represented boundary;
    /// include an application tolerance in <see cref="GeoBounds.RadiusMeters"/> when that distinction matters.
    /// </remarks>
    public static Expression<Func<TEntity, bool>> WithinRadius<TEntity>(
        Expression<Func<TEntity, double>> latitudeSelector,
        Expression<Func<TEntity, double>> longitudeSelector,
        GeoBounds bounds)
        where TEntity : class
    {
        var (parameter, latitude, longitude) = PrepareSelectors(latitudeSelector, longitudeSelector);
        ArgumentNullException.ThrowIfNull(bounds);
        var withinBounds = BuildBounds(latitude, longitude, bounds);
        var withinDistance = Expression.LessThanOrEqual(
            BuildDistance(latitude, longitude, bounds.Center),
            CapturedValue(bounds.RadiusMeters));
        return Expression.Lambda<Func<TEntity, bool>>(
            Expression.AndAlso(withinBounds, withinDistance),
            parameter);
    }

    private static (ParameterExpression Parameter, Expression Latitude, Expression Longitude)
        PrepareSelectors<TEntity>(
            Expression<Func<TEntity, double>> latitudeSelector,
            Expression<Func<TEntity, double>> longitudeSelector)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(latitudeSelector);
        ArgumentNullException.ThrowIfNull(longitudeSelector);
        var parameter = Expression.Parameter(typeof(TEntity), "row");
        return (
            parameter,
            Replace(latitudeSelector.Body, latitudeSelector.Parameters[0], parameter),
            Replace(longitudeSelector.Body, longitudeSelector.Parameters[0], parameter));
    }

    private static Expression BuildBounds(Expression latitude, Expression longitude, GeoBounds bounds)
    {
        Expression? predicate = null;
        foreach (var box in bounds.Boxes)
        {
            var withinBox = Expression.AndAlso(
                Expression.AndAlso(
                    Expression.GreaterThanOrEqual(latitude, CapturedValue(box.MinimumLatitude)),
                    Expression.LessThanOrEqual(latitude, CapturedValue(box.MaximumLatitude))),
                Expression.AndAlso(
                    Expression.GreaterThanOrEqual(longitude, CapturedValue(box.MinimumLongitude)),
                    Expression.LessThanOrEqual(longitude, CapturedValue(box.MaximumLongitude))));
            predicate = predicate is null ? withinBox : Expression.OrElse(predicate, withinBox);
        }

        return predicate ?? throw new InvalidOperationException("Geographic bounds must contain at least one box.");
    }

    private static ConditionalExpression BuildDistance(Expression latitude, Expression longitude, GeoCoordinate origin)
    {
        var zero = Expression.Constant(0d);
        var one = Expression.Constant(1d);
        var originLatitude = CapturedValue(origin.Latitude);
        var longitudeDelta = Expression.Subtract(longitude, CapturedValue(origin.Longitude));
        var equivalentAntimeridian = Expression.OrElse(
            Expression.Equal(longitudeDelta, Expression.Constant(360d)),
            Expression.Equal(longitudeDelta, Expression.Constant(-360d)));
        var normalizedLongitudeDelta = Expression.Condition(
            equivalentAntimeridian,
            zero,
            longitudeDelta);
        var latitudeSine = Expression.Call(
            SinMethod,
            Expression.Multiply(
                Expression.Subtract(latitude, originLatitude),
                Expression.Constant(DegreesToRadians / 2)));
        var longitudeSine = Expression.Call(
            SinMethod,
            Expression.Multiply(
                normalizedLongitudeDelta,
                Expression.Constant(DegreesToRadians / 2)));
        var latitudeSquare = Expression.Multiply(latitudeSine, latitudeSine);
        var longitudeSquare = Expression.Multiply(longitudeSine, longitudeSine);
        var latitudeAtPole = Expression.OrElse(
            Expression.Equal(latitude, Expression.Constant(90d)),
            Expression.Equal(latitude, Expression.Constant(-90d)));
        var latitudeCosine = Expression.Condition(
            latitudeAtPole,
            zero,
            Expression.Call(
                CosMethod,
                Expression.Multiply(latitude, Expression.Constant(DegreesToRadians))));
        var haversine = Expression.Add(
            latitudeSquare,
            Expression.Multiply(
                Expression.Multiply(
                    CapturedValue(double.CosPi(origin.Latitude / 180)),
                    latitudeCosine),
                longitudeSquare));
        var clamped = Expression.Condition(
            Expression.LessThan(haversine, zero),
            zero,
            Expression.Condition(
                Expression.GreaterThan(haversine, one),
                one,
                haversine));
        var distance = Expression.Multiply(
            Expression.Constant(2 * GeoCoordinate.MeanEarthRadiusMeters),
            Expression.Call(
                AsinMethod,
                Expression.Call(SqrtMethod, clamped)));
        var originAtPole = Expression.OrElse(
            Expression.Equal(originLatitude, Expression.Constant(90d)),
            Expression.Equal(originLatitude, Expression.Constant(-90d)));
        var samePole = Expression.AndAlso(
            originAtPole,
            Expression.Equal(latitude, originLatitude));
        return Expression.Condition(samePole, zero, distance);
    }

    private static Expression CapturedValue(double value)
    {
        Expression<Func<double>> captured = () => value;
        return captured.Body;
    }

    private static Expression Replace(
        Expression body,
        ParameterExpression original,
        ParameterExpression replacement) =>
        new ParameterReplacement(original, replacement).Visit(body);

    private static MethodInfo GetMathMethod(string name) =>
        typeof(Math).GetMethod(name, BindingFlags.Public | BindingFlags.Static, [typeof(double)])
        ?? throw new InvalidOperationException($"Math.{name}(double) was not found.");

    private sealed class ParameterReplacement(
        ParameterExpression original,
        ParameterExpression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == original ? replacement : node;
    }
}