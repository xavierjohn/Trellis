namespace Trellis.Asp.Tests;

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis;
using Trellis.Asp.Validation;
using Xunit;

/// <summary>
/// Issue #664: index-precise validation field paths must work under Native AOT, at parity with the
/// reflection pipeline fixed in #658.
/// </summary>
/// <remarks>
/// <para>
/// Under Native AOT the type-info modifier cannot call <c>Type.MakeGenericType</c>, so the closed
/// path-tracking converters are created at compile time by the source generator and handed to
/// <see cref="ScalarValuePathTracking"/>. These tests exercise exactly that resolution path by
/// suppressing dynamic path-converter construction, which is what makes them meaningful on a JIT
/// test host: without the suppression the reflection pipeline would produce the right answer for the
/// wrong reason.
/// </para>
/// <para>
/// The DTO types here are unique to this class so the process-wide registry cannot be perturbed by,
/// or perturb, tests running in parallel.
/// </para>
/// </remarks>
[Collection(nameof(AotPathTrackingTests))]
public sealed class AotPathTrackingTests : IDisposable
{
    public sealed class Email : ScalarValueObject<Email, string>, IScalarValue<Email, string>
    {
        private Email(string value) : base(value) { }

        public static Result<Email> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "email";
            return string.IsNullOrWhiteSpace(value) || !value.Contains('@')
                ? Result.Fail<Email>(new Error.InvalidInput(EquatableArray.Create(
                    new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = "Email address is not valid." })))
                : Result.Ok(new Email(value));
        }
    }

    public sealed record AotMemberDto(Email Email);

    public sealed record AotTeamCommand(List<AotMemberDto> Members);

    public sealed record AotEmailListCommand(IReadOnlyList<Email> Emails);

    public sealed record AotAddressDto(Email Email);

    public sealed record AotPersonCommand(AotAddressDto Contact);

    public void Dispose() => ScalarValuePathTracking.ClearForTests();

    private static JsonSerializerOptions BuildOptions()
    {
        var services = new ServiceCollection();
        services.AddScalarValueValidationForMinimalApi();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
    }

    [Fact]
    public void Without_generated_registrations_the_aot_path_reports_only_the_leaf_name()
    {
        ScalarValuePathTracking.ClearForTests();

        using var aot = ServiceCollectionExtensions.SuppressDynamicPathConverterConstructionForTests();
        var options = BuildOptions();
        const string json = """{ "members": [ { "email": "not-an-email" } ] }""";

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<AotTeamCommand>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields[0].Field.Path.Should().Be(
                "/email",
                "this is the pre-#664 Native AOT behaviour the generated registrations exist to fix");
        }
    }

    [Fact]
    public void Registered_collection_reports_the_index_precise_path_without_runtime_generic_construction()
    {
        ScalarValuePathTracking.ClearForTests();
        ScalarValuePathTracking.RegisterCollection<List<AotMemberDto>, AotMemberDto>();

        using var aot = ServiceCollectionExtensions.SuppressDynamicPathConverterConstructionForTests();
        var options = BuildOptions();
        const string json = """{ "members": [ { "email": "ada@x.com" }, { "email": "not-an-email" } ] }""";

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<AotTeamCommand>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle();
            error.Fields[0].Field.Path.Should().Be("/members/1/email");
        }
    }

    [Theory]
    [InlineData("""{ "emails": [ "ada@x.com", "not-an-email" ] }""", "/emails/1")]
    [InlineData("""{ "emails": [ null ] }""", "/emails/0")]
    public void Registered_direct_scalar_collection_reports_the_element_path_without_runtime_generic_construction(
        string json,
        string expectedPath)
    {
        ScalarValuePathTracking.ClearForTests();
        ScalarValuePathTracking.RegisterCollection<IReadOnlyList<Email>, Email>();

        using var aot = ServiceCollectionExtensions.SuppressDynamicPathConverterConstructionForTests();
        var options = BuildOptions();

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<AotEmailListCommand>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle();
            error.Fields[0].Field.Path.Should().Be(expectedPath);
        }
    }

    [Fact]
    public void Registered_nested_object_reports_the_full_path_without_runtime_generic_construction()
    {
        ScalarValuePathTracking.ClearForTests();
        ScalarValuePathTracking.RegisterObject<AotAddressDto>();

        using var aot = ServiceCollectionExtensions.SuppressDynamicPathConverterConstructionForTests();
        var options = BuildOptions();
        const string json = """{ "contact": { "email": "not-an-email" } }""";

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<AotPersonCommand>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle();
            error.Fields[0].Field.Path.Should().Be("/contact/email");
        }
    }

    [Fact]
    public void Registrations_leave_round_trip_serialization_unchanged()
    {
        ScalarValuePathTracking.ClearForTests();
        ScalarValuePathTracking.RegisterCollection<List<AotMemberDto>, AotMemberDto>();

        using var aot = ServiceCollectionExtensions.SuppressDynamicPathConverterConstructionForTests();
        var options = BuildOptions();
        const string json = """{"members":[{"email":"ada@x.com"}]}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var command = JsonSerializer.Deserialize<AotTeamCommand>(json, options);

            ValidationErrorsContext.HasErrors.Should().BeFalse();
            JsonSerializer.Serialize(command, options).Should().Be(json);
        }
    }
}