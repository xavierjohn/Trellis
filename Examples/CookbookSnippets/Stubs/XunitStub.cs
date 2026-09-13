// Compile-only stand-in for the xUnit attributes in Recipes 10 and 26.
// A distinct namespace prevents collisions with the real runner and analyzers
// in CookbookSnippets.Tests, which executes behavioral regressions against this library.
namespace CookbookSnippets.Compilation;

using System;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class FactAttribute : Attribute
{
    public string? DisplayName { get; set; }
    public string? Skip { get; set; }
}