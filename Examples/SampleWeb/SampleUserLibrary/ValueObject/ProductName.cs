using Trellis;

namespace SampleUserLibrary;

[StringLength(200)]
public partial class ProductName : RequiredString<ProductName>
{
}