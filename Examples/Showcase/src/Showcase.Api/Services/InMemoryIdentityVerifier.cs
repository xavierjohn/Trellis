namespace Trellis.Showcase.Api.Services;

using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Domain.ValueObjects;

/// <summary>
/// Deterministic identity verifier. Boundary mapping:
/// <list type="bullet">
///   <item><description>Missing code → <see cref="Error.Unauthorized"/>.</description></item>
///   <item><description>Malformed code (not exactly six digits) → <see cref="Error.UnprocessableContent"/>.</description></item>
///   <item><description>Code rejected (<c>000000</c>) → <see cref="Error.Unauthorized"/>.</description></item>
/// </list>
/// </summary>
public sealed class InMemoryIdentityVerifier : IIdentityVerifier
{
    public Task<Result> VerifyAsync(CustomerId customerId, string verificationCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(verificationCode))
        {
            return Task.FromResult<Result>(Result.Fail(new Error.Unauthorized()
            {
                Detail = "Verification code is required.",
            }));
        }

        if (verificationCode.Length != 6 || !verificationCode.All(char.IsDigit))
        {
            return Task.FromResult<Result>(Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty("verificationCode"), "validation.format")
                {
                    Detail = "Verification code must be exactly six digits.",
                }))));
        }

        if (verificationCode == "000000")
        {
            return Task.FromResult<Result>(Result.Fail(new Error.Unauthorized()
            {
                Detail = "Verification code rejected.",
            }));
        }

        return Task.FromResult(Result.Ok());
    }
}
