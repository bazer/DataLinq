using System.Threading.Tasks;
using DataLinq.Metadata;

namespace DataLinq.Tests.Unit.Core;

public class CsTypeDeclarationTests
{
    [Test]
    public async Task Constructor_FromRuntimeEnumType_ClassifiesAsEnum()
    {
        var declaration = new CsTypeDeclaration(typeof(RuntimeEnum));

        await Assert.That(declaration.ModelCsType).IsEqualTo(ModelCsType.Enum);
    }

    private enum RuntimeEnum
    {
        Value = 1
    }
}
