using System.Threading.Tasks;
using DataLinq.Extensions.Helpers;

namespace DataLinq.Tests.Unit.Core;

public class StringExtensionsTests
{
    [Test]
    public async Task ToCSharpIdentifier_SanitizesProviderNames()
    {
        await Assert.That("order-items".ToCSharpIdentifier(pascalCase: true)).IsEqualTo("OrderItems");
        await Assert.That("ship.to".ToCSharpIdentifier(pascalCase: true)).IsEqualTo("ShipTo");
        await Assert.That("2fa code".ToCSharpIdentifier(pascalCase: true)).IsEqualTo("_2faCode");
        await Assert.That("class".ToCSharpIdentifier(pascalCase: false)).IsEqualTo("_class");
        await Assert.That("RakenskapsarFK".ToCSharpIdentifier(pascalCase: true)).IsEqualTo("RakenskapsarFK");
    }

    [Test]
    public async Task ToCamelCase_ProducesValidParameterNames()
    {
        await Assert.That("Class".ToCamelCase()).IsEqualTo("_class");
        await Assert.That("_2faCode".ToCamelCase()).IsEqualTo("_2faCode");
    }
}
