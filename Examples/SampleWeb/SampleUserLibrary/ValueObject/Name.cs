using Trellis;

namespace SampleUserLibrary;

/// <summary>
/// A generic name value object that can be used for any name field.
/// This demonstrates that the same value object type can be used for
/// multiple properties (e.g., FirstName, LastName) with correct field names.
/// </summary>
[StringLength(100)]
public partial class Name : RequiredString<Name>
{
}