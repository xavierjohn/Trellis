namespace SampleUserLibrary;

using Trellis;

[StringLength(100)]
public partial class LastName : RequiredString<LastName>
{
}