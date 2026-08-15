namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Routing;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Argument validation and basic surface coverage for <see cref="HttpResponseOptionsBuilder{T}"/>
/// and the non-generic <see cref="HttpResponseOptionsBuilder"/>. These don't exercise the
/// IResult execution path (covered elsewhere) but pin the builder API contract.
/// </summary>
public sealed class HttpResponseOptionsBuilderTests
{
    private sealed record Thing(int Id);

    [Fact]
    public void WithETag_string_overload_throws_on_null_selector()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        FluentActions.Invoking(() => b.WithETag((Func<Thing, string>)null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithETag_value_overload_throws_on_null_selector()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        FluentActions.Invoking(() => b.WithETag((Func<Thing, EntityTagValue>)null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithLastModified_throws_on_null_selector()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        FluentActions.Invoking(() => b.WithLastModified(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithContentLocation_throws_on_null_selector()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        FluentActions.Invoking(() => b.WithContentLocation(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Vary_throws_on_null_array_but_skips_blank_entries()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        FluentActions.Invoking(() => b.Vary(null!)).Should().Throw<ArgumentNullException>();
        b.Vary("", "  ", "Accept").Should().BeSameAs(b);
    }

    [Fact]
    public void WithContentLanguage_throws_on_null_array_but_skips_blank_entries()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        FluentActions.Invoking(() => b.WithContentLanguage(null!)).Should().Throw<ArgumentNullException>();
        b.WithContentLanguage("", "  ", "en").Should().BeSameAs(b);
    }

    [Fact]
    public void Created_literal_throws_on_null_or_whitespace()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        FluentActions.Invoking(() => b.Created((string)null!)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => b.Created("   ")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Created_selector_throws_on_null()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        FluentActions.Invoking(() => b.Created((Func<Thing, string>)null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreatedAtRoute_validates_arguments()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        FluentActions.Invoking(() => b.CreatedAtRoute(null!, _ => new RouteValueDictionary()))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => b.CreatedAtRoute("  ", _ => new RouteValueDictionary()))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => b.CreatedAtRoute("foo", null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreatedAtAction_validates_arguments()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        FluentActions.Invoking(() => b.CreatedAtAction(null!, _ => new RouteValueDictionary()))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => b.CreatedAtAction("  ", _ => new RouteValueDictionary()))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => b.CreatedAtAction("Get", null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithRouteValueResolver_validates_arguments()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        FluentActions.Invoking(() => b.WithRouteValueResolver(null!, _ => "v"))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => b.WithRouteValueResolver("  ", _ => "v"))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => b.WithRouteValueResolver("api-version", null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithRouteValueResolver_returns_builder_for_chaining()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        b.CreatedAtRoute("Get", _ => new RouteValueDictionary())
            .WithRouteValueResolver("api-version", _ => "2026-11-12")
            .WithRouteValueResolver("tenant", _ => "acme")
            .Should().BeSameAs(b);
    }

    [Fact]
    public void Builder_chain_returns_same_builder_for_fluent_use()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        b.WithETag(_ => "x")
            .WithLastModified(_ => DateTimeOffset.UtcNow)
            .Vary("Accept")
            .WithContentLanguage("en")
            .WithContentLocation(_ => "/x")
            .Created("/x")
            .EvaluatePreconditions()
            .HonorPrefer()
            .WithErrorMapping(_ => 500)
            .WithErrorMapping<Error.Conflict>(409)
            .Should().BeSameAs(b);
    }

    [Fact]
    public void NonGeneric_builder_chains_and_supports_overrides()
    {
        var b = new HttpResponseOptionsBuilder();
        FluentActions.Invoking(() => b.Vary(null!)).Should().Throw<ArgumentNullException>();
        b.Vary("", "  ", "Accept")
            .HonorPrefer()
            .WithErrorMapping(_ => 500)
            .WithErrorMapping<Error.Conflict>(409)
            .Should().BeSameAs(b);
    }

    // A delegate mapper cannot fail fast because `default` (0) is the documented "not mine" arm,
    // so the resolver skips out-of-range results at resolution time. A typed override has no such
    // idiom — every registration is a deliberate request — so an unwritable status must be rejected
    // at composition time instead of silently reaching the response.
    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(600)]
    [InlineData(-1)]
    public void WithErrorMapping_typed_overload_rejects_out_of_range_status(int statusCode)
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        FluentActions.Invoking(() => b.WithErrorMapping<Error.Conflict>(statusCode))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(600)]
    public void NonGeneric_WithErrorMapping_typed_overload_rejects_out_of_range_status(int statusCode)
    {
        var b = new HttpResponseOptionsBuilder();
        FluentActions.Invoking(() => b.WithErrorMapping<Error.Conflict>(statusCode))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(100)]
    [InlineData(410)]
    [InlineData(599)]
    public void WithErrorMapping_typed_overload_accepts_in_range_status(int statusCode)
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        b.WithErrorMapping<Error.Conflict>(statusCode).Should().BeSameAs(b);
    }
}