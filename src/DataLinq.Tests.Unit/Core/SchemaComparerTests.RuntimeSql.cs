using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Metadata;
using DataLinq.Validation;

namespace DataLinq.Tests.Unit.Core;

public partial class SchemaComparerTests
{
    [Test]
    [Arguments("`value` > 0", "((`value`   >  0))", true)]
    [Arguments("`value` <> 'A  B'", "(`value` <> 'A B')", false)]
    [Arguments("`value` <> 'A''  B'", "(`value` <> 'A'' B')", false)]
    [Arguments("(`value` > 0) AND (`value` < 2)", "`value` > 0) AND (`value` < 2", false)]
    [Arguments("value > 0", "`value` > 0", false)]
    [Arguments("`value` <> ') ('", "((`value` <> ') ('))", true)]
    [Arguments("`value` > 0 -- keep newline\n AND 1=1", "`value` > 0 -- keep newline AND 1=1", false)]
    [Arguments("`value` <> 'A\\ B'", "`value` <> 'A\\  B'", false)]
    public async Task RuntimeSql_CheckNormalizationPreservesLiteralAndGroupingMeaning(string modelExpression, string liveExpression, bool equivalent)
    {
        DatabaseDefinition Data(string expression) => CreateDatabase(CreateTable("values",
            [CreateColumn("value", typeof(int), false, primaryKey: true)], [new CheckAttribute("CK", expression)]));
        var differences = RuntimeCompare(Data(modelExpression), Data(liveExpression), SchemaIdentifierRules.Server(DatabaseType.MySQL, 1));
        await Assert.That(differences.Count).IsEqualTo(equivalent ? 0 : 2);
    }

    [Test]
    public async Task RuntimeSql_DefaultExpressionPreservesWhitespaceInsideLiterals()
    {
        DatabaseDefinition Data(string expression) => CreateDatabase(CreateTable("values",
            [CreateColumn("id", typeof(int), false, primaryKey: true), CreateColumn("value", typeof(string), false,
                attributes: [new DefaultSqlAttribute(DatabaseType.MySQL, expression)])]));
        var differences = RuntimeCompare(Data("concat('A  B', 'C')"), Data("concat('A B', 'C')"), SchemaIdentifierRules.Server(DatabaseType.MySQL, 1));
        await Assert.That(differences.Single().Kind).IsEqualTo(SchemaDifferenceKind.ColumnDefaultMismatch);
    }
}
