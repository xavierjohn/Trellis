namespace Trellis.Analyzers.Tests;

/// <summary>
/// Shared stub source for <c>Trellis.EntityFrameworkCore.OwnedEntityAttribute</c> used in
/// TRLS022 (<see cref="OwnedEntityInitOnlyPropertyAnalyzer"/>) tests.
/// </summary>
public static class OwnedEntityTestStubs
{
    /// <summary>
    /// Stub source providing <c>Trellis.EntityFrameworkCore.OwnedEntityAttribute</c>.
    /// </summary>
    public const string Source = """
        namespace Trellis.EntityFrameworkCore
        {
            using System;

            [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
            public sealed class OwnedEntityAttribute : Attribute;
        }
        """;
}
