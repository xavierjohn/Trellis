namespace Trellis.Asp.Tests;

using System.Net.Http.Headers;
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
    public void WithRange_selector_throws_on_null()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        FluentActions.Invoking(() => b.WithRange((Func<Thing, ContentRangeHeaderValue>)null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Both_WithRange_overloads_are_chainable()
    {
        var b = new HttpResponseOptionsBuilder<Thing>();
        b.WithRange(0, 1, 2).WithRange(_ => new ContentRangeHeaderValue(10))
            .WithRange(0, 5, 10).Should().BeSameAs(b);
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
            .WithAcceptRanges("bytes")
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
}