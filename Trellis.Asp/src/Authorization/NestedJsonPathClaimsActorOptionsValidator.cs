namespace Trellis.Asp.Authorization;

using System.Collections.Generic;
using Microsoft.Extensions.Options;

internal sealed class NestedJsonPathClaimsActorOptionsValidator : IValidateOptions<NestedJsonPathClaimsActorOptions>
{
    public ValidateOptionsResult Validate(string? name, NestedJsonPathClaimsActorOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ContainerClaim)
            && (!string.IsNullOrWhiteSpace(options.ActorIdPath) || !string.IsNullOrWhiteSpace(options.PermissionsPath)))
        {
            errors.Add(
                "NestedJsonPathClaimsActorOptions.ContainerClaim must be set when " +
                "NestedJsonPathClaimsActorOptions.ActorIdPath or " +
                "NestedJsonPathClaimsActorOptions.PermissionsPath is configured.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
