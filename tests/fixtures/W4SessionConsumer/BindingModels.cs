using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace SessionConsumer;

[Database("session_consumer")]
public partial class TestDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<TestRow> Rows { get; } = new(source);
}

[Table("rows")]
public abstract partial class TestRow(IRowData row, IDataSourceAccess source)
    : Immutable<TestRow, TestDb>(row, source), ITableModel<TestDb>
{
    [PrimaryKey, Column("id")] public abstract int Id { get; }
    [Column("value")] public abstract string Value { get; }
}
