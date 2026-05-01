using System.Threading.Tasks;
using DataLinq.Metadata;

namespace DataLinq.Tests.Unit.Core;

public class SqlGenerationTests
{
    [Test]
    public async Task CreateTable_Check_WrapsWholeExpressionWhenOuterParenthesesDoNot()
    {
        var sql = new SqlGeneration();

        sql.CreateTable("checked_table", table =>
        {
            table.NewRow().Indent().Add("`id` INT");
            table.Check("CK_checked_table_range", "(`minimum` >= 0) AND (`maximum` >= `minimum`)");
        });

        await Assert.That(sql.sql.Text)
            .Contains("CONSTRAINT `CK_checked_table_range` CHECK ((`minimum` >= 0) AND (`maximum` >= `minimum`))");
    }
}
