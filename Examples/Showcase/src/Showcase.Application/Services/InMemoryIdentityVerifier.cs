namespace Trellis.Showcase.Application.Services;

using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Domain.ValueObjects;

/// <summary>
/// Deterministic identity verifier. Boundary mapping:
/// <list type="bullet">
///   <item><description>Missing code → <see cref="Error.AuthenticationRequired"/>.</description></item>
///   <item><description>Malformed code (not exactly six digits) → <see cref="Error.InvalidInput"/>.</description></item>
///   <item><description>Code rejected (<c>000000</c>) → <see cref="Error.AuthenticationRequired"/>.</description></item>
/// </list>
/// </summary>
public sealed class InMemoryIdentityVerifier : IIdentityVerifier
{
    public Task<Result<Unit>> VerifyAsync(CustomerId customerId, string verificationCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(verificationCode))
        {
            return Task.FromResult(Result.Fail(Unauthorized("Verification code is required.")));
        }

        if (verificationCode.Length != 6 || !verificationCode.All(char.IsDigit))
        {
            return Task.FromResult(Result.Fail(Error.InvalidInput.ForField(
                "verificationCode",
                "validation.format",
                "Verification code must be exactly six digits.")));
        }

        if (verificationCode == "000000")
        {
            return Task.FromResult(Result.Fail(Unauthorized("Verification code rejected.")));
        }

        return Task.FromResult(Result.Ok());
    }

    private static Error.AuthenticationRequired Unauthorized(string detail) =>
        new()
        {
            Detail = detail,
        };
}