using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class TypeAttributeConstantMetadataTests
{
    [Test]
    public async Task CompiledModel_ExposesResolvedConstantLength()
    {
        var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel<ConstantLengthDb>().ValueOrException();
        var type = metadata.TableModels.Single().Table.Columns.Single(x => x.DbName == "reference").DbTypes.Single();
        await Assert.That(type.Name).IsEqualTo("varchar");
        await Assert.That(type.DatabaseType).IsEqualTo(DatabaseType.MariaDB);
        await Assert.That(type.Length).IsEqualTo((ulong?)1000);
    }
}

public static class ConstantColumnLimits
{
    public const int MaxReferenceLength = 1000;
}

[Database("constant_lengths")]
public partial class ConstantLengthDb(DataSourceAccess source) : IDatabaseModel<ConstantLengthDb>
{
    public DbRead<ConstantLengthRow> Rows { get; } = new(source);
}

[Table("constant_rows")]
public abstract partial class ConstantLengthRow(IRowData data, IDataSourceAccess source)
    : Immutable<ConstantLengthRow, ConstantLengthDb>(data, source), ITableModel<ConstantLengthDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [Column("reference"), Type(DatabaseType.MariaDB, "varchar", ConstantColumnLimits.MaxReferenceLength)]
    public abstract string Reference { get; }
}
