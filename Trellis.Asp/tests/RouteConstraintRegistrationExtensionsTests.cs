namespace Trellis.Asp.Tests;

using System;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis;
using Trellis.Asp.Routing;
using Xunit;

/// <summary>
/// Tests for auto-registration of Trellis value object route constraints.
/// </summary>
public class RouteConstraintRegistrationExtensionsTests
{
    public sealed class ProductId
        : ScalarValueObject<ProductId, string>,
          IScalarValue<ProductId, string>,
          IParsable<ProductId>
    {
        private ProductId(string value) : base(value) { }

        public static Result<ProductId> TryCreate(string? value, string? fieldName = null)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 3)
                return Result.Fail<ProductId>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "id"), "validation.error") { Detail = "ProductId must be at least 3 characters." })));
            return Result.Ok(new ProductId(value));
        }

        public static ProductId Parse(string s, IFormatProvider? provider)
        {
            var r = TryCreate(s);
            if (r.IsSuccess)
                return r.Value;
            throw new FormatException("Invalid ProductId.");
        }

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out ProductId result)
        {
            var r = TryCreate(s);
            if (r.IsSuccess)
            {
                result = r.Value;
                return true;
            }

            result = null;
            return false;
        }
    }

    [Fact]
    public void TrellisValueObjectRouteConstraint_Match_ReturnsTrue_ForValidValue()
    {
        var constraint = new TrellisValueObjectRouteConstraint<ProductId>();
        var values = new RouteValueDictionary { ["id"] = "abc123" };

        var matched = constraint.Match(httpContext: null, route: null, "id", values, RouteDirection.IncomingRequest);

        matched.Should().BeTrue();
    }

    [Fact]
    public void TrellisValueObjectRouteConstraint_Match_ReturnsFalse_ForInvalidValue()
    {
        var constraint = new TrellisValueObjectRouteConstraint<ProductId>();
        var values = new RouteValueDictionary { ["id"] = "no" };

        var matched = constraint.Match(httpContext: null, route: null, "id", values, RouteDirection.IncomingRequest);

        matched.Should().BeFalse();
    }

    [Fact]
    public void TrellisValueObjectRouteConstraint_Match_ReturnsFalse_WhenKeyMissing()
    {
        var constraint = new TrellisValueObjectRouteConstraint<ProductId>();
        var values = new RouteValueDictionary();

        var matched = constraint.Match(httpContext: null, route: null, "id", values, RouteDirection.IncomingRequest);

        matched.Should().BeFalse();
    }

    [Fact]
    public void TrellisValueObjectRouteConstraint_Match_ReturnsFalse_WhenValueIsNull()
    {
        var constraint = new TrellisValueObjectRouteConstraint<ProductId>();
        var values = new RouteValueDictionary { ["id"] = null };

        var matched = constraint.Match(httpContext: null, route: null, "id", values, RouteDirection.IncomingRequest);

        matched.Should().BeFalse();
    }

    [Fact]
    public void AddTrellisRouteConstraint_RegistersConstraintUnderTypeName()
    {
        var services = new ServiceCollection();
        services.AddTrellisRouteConstraint<ProductId>();

        var routeOptions = services.BuildServiceProvider().GetRequiredService<IOptions<RouteOptions>>().Value;

        routeOptions.ConstraintMap.Should().ContainKey(nameof(ProductId));
        routeOptions.ConstraintMap[nameof(ProductId)].Should().Be<TrellisValueObjectRouteConstraint<ProductId>>();
    }

    [Fact]
    public void AddTrellisRouteConstraint_RegistersConstraintUnderCustomName()
    {
        var services = new ServiceCollection();
        services.AddTrellisRouteConstraint<ProductId>("pid");

        var routeOptions = services.BuildServiceProvider().GetRequiredService<IOptions<RouteOptions>>().Value;

        routeOptions.ConstraintMap.Should().ContainKey("pid");
    }

    [Fact]
    public void AddTrellisRouteConstraints_DiscoversValueObjectsInAssembly()
    {
        var services = new ServiceCollection();
        services.AddTrellisRouteConstraints(typeof(RouteConstraintRegistrationExtensionsTests).Assembly);

        var routeOptions = services.BuildServiceProvider().GetRequiredService<IOptions<RouteOptions>>().Value;

        routeOptions.ConstraintMap.Should().ContainKey(nameof(ProductId));
    }

    [Fact]
    public void AddTrellisRouteConstraints_DoesNotOverrideExistingMapping()
    {
        var services = new ServiceCollection();
        services.Configure<RouteOptions>(o => o.ConstraintMap[nameof(ProductId)] = typeof(IntRouteConstraint));
        services.AddTrellisRouteConstraints(typeof(RouteConstraintRegistrationExtensionsTests).Assembly);

        var routeOptions = services.BuildServiceProvider().GetRequiredService<IOptions<RouteOptions>>().Value;

        routeOptions.ConstraintMap[nameof(ProductId)].Should().Be<IntRouteConstraint>();
    }
}
