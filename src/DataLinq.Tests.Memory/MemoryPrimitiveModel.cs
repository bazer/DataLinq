using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Memory;

[UseCache]
[Database("memory_spike")]
public sealed partial class MemoryPrimitiveDatabase(IDataLinqReadSource readSource) : IDatabaseModel
{
    public DbRead<MemoryPrimitiveRow> Rows { get; } = new(readSource);
}

[Table("memory_primitive_rows")]
public abstract partial class MemoryPrimitiveRow :
    Immutable<MemoryPrimitiveRow, MemoryPrimitiveDatabase>,
    ITableModel<MemoryPrimitiveDatabase>
{
    protected MemoryPrimitiveRow(
        IRowData rowData,
        IDataSourceAccess dataSource)
        : base(rowData, dataSource)
    {
    }

    protected MemoryPrimitiveRow(
        IRowData rowData,
        IDataLinqReadSource readSource)
        : base(rowData, readSource)
    {
    }

    [PrimaryKey]
    [Column("id")]
    public abstract int Id { get; }

    [Column("group_id")]
    public abstract int GroupId { get; }

    [Column("name")]
    public abstract string Name { get; }
}
