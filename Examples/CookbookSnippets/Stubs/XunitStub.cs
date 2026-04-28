// Minimal stub of the Xunit attributes used by Recipe 10.
// The CookbookSnippets project is a non-test library that only needs to compile
// every cookbook recipe; runtime test execution is not in scope. Instead of
// pulling xunit.v3 (which forces OutputType=Exe and a runner), we stub the few
// attribute types referenced by the recipe. If the cookbook ever ships into a
// real test project the consumer will already have `xunit` referenced and these
// stubs do not interfere because they live in this assembly only.
namespace Xunit;

using System;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class FactAttribute : Attribute
{
    public string? DisplayName { get; set; }
    public string? Skip { get; set; }
}