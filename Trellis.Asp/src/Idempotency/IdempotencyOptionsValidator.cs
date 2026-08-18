namespace Trellis.Asp.Idempotency;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

internal sealed class IdempotencyOptionsValidator : IValidateOptions<IdempotencyOptions>
{
    public ValidateOptionsResult Validate(string? name, IdempotencyOptions options)
    {
        // IValidateOptions<T> is global across all named instances of T. AddTrellisIdempotency
        // only registers + consumes the default options instance, so any other named instance
        // belongs to the consumer and we must not impose Trellis-specific invariants on it.
        if (name != Options.DefaultName)
            return ValidateOptionsResult.Skip;

        var errors = new List<string>();

        ValidateHeaderName(options.HeaderName, nameof(IdempotencyOptions.HeaderName), errors);
        ValidateHeaderName(options.ReplayHeaderName, nameof(IdempotencyOptions.ReplayHeaderName), errors);

        if (options.Ttl <= TimeSpan.Zero)
            errors.Add("IdempotencyOptions.Ttl must be greater than TimeSpan.Zero.");

        if (options.ReservationTimeout <= TimeSpan.Zero)
            errors.Add("IdempotencyOptions.ReservationTimeout must be greater than TimeSpan.Zero.");

        if (options.MaxKeyLength <= 0)
            errors.Add("IdempotencyOptions.MaxKeyLength must be greater than 0.");

        if (options.MaxRequestBodyBytes <= 0)
            errors.Add("IdempotencyOptions.MaxRequestBodyBytes must be greater than 0.");

        if (options.MaxResponseBodyBytes <= 0)
            errors.Add("IdempotencyOptions.MaxResponseBodyBytes must be greater than 0.");

        if (options.MismatchStatusCode is < 400 or > 599)
            errors.Add("IdempotencyOptions.MismatchStatusCode must be between 400 and 599.");

        if (options.Methods.Count == 0)
        {
            errors.Add("IdempotencyOptions.Methods must contain at least one HTTP method.");
        }
        else if (ContainsInvalidHttpToken(options.Methods))
        {
            errors.Add("IdempotencyOptions.Methods must contain only valid HTTP method tokens.");
        }

        if (ContainsInvalidHttpToken(options.AdditionalFingerprintHeaders))
            errors.Add("IdempotencyOptions.AdditionalFingerprintHeaders must contain only valid HTTP header names.");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateHeaderName(string? value, string propertyName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"IdempotencyOptions.{propertyName} must be set to a non-empty header name.");
            return;
        }

        if (!IsHttpToken(value))
            errors.Add($"IdempotencyOptions.{propertyName} must be a valid HTTP header name.");
    }

    private static bool ContainsInvalidHttpToken(IEnumerable<string> values) =>
        values.Any(value => string.IsNullOrWhiteSpace(value) || !IsHttpToken(value));

    private static bool IsHttpToken(string value) => value.All(IsHttpTokenChar);

    private static bool IsHttpTokenChar(char ch) =>
        ch is (>= 'A' and <= 'Z')
            or (>= 'a' and <= 'z')
            or (>= '0' and <= '9')
            or '!'
            or '#'
            or '$'
            or '%'
            or '&'
            or '\''
            or '*'
            or '+'
            or '-'
            or '.'
            or '^'
            or '_'
            or '`'
            or '|'
            or '~';
}