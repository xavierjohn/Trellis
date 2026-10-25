namespace Trellis.TestSupport;

// Linked into test projects whose fixtures read configuration through an injected Func<string, string?>.
internal static class FakeEnvironment
{
    /// <summary>Creates an environment reader over fixed values; any name not listed reads as unset.</summary>
    internal static Func<string, string?> With(params (string Name, string? Value)[] variables)
    {
        var values = variables.ToDictionary(variable => variable.Name, variable => variable.Value);
        return name => values.GetValueOrDefault(name);
    }
}
