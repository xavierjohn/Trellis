using Trellis;

namespace SampleUserLibrary;

[StringLength(100)]
public partial class FirstName : RequiredString<FirstName>
{
}