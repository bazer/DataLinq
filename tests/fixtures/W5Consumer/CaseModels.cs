using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace W5Consumer;

[Database("case_model")]
public partial class CaseDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<CaseParent> Parents { get; } = new(source);
    public DbRead<CaseChild> Children { get; } = new(source);
    public DbRead<CaseOrder> Orders { get; } = new(source);
}

[Table("ÅParent"), Comment("A"), Check("CK_Positive", "`ID` > 0")]
public abstract partial class CaseParent(IRowData row, IDataSourceAccess source)
    : Immutable<CaseParent, CaseDb>(row, source), ITableModel<CaseDb>
{
    [PrimaryKey, Column("ID")] public abstract int Id { get; }
    [Column("Value"), Default("A"), Comment("A")]
    [Type(DatabaseType.MySQL, "varchar", 40), Type(DatabaseType.MariaDB, "varchar", 40)]
    [Index("IX_Value", IndexCharacteristic.Unique, IndexType.BTREE)]
    public abstract string Value { get; }
    [Relation("Child", "ParentID", "FK_Parent")]
    public abstract IImmutableRelation<CaseChild> Children { get; }
}

[Table("Child")]
public abstract partial class CaseChild(IRowData row, IDataSourceAccess source)
    : Immutable<CaseChild, CaseDb>(row, source), ITableModel<CaseDb>
{
    [PrimaryKey, Column("ID")] public abstract int Id { get; }
    [Column("ParentID"), ForeignKey("ÅParent", "ID", "FK_Parent")]
    public abstract int ParentId { get; }
    [Relation("ÅParent", "ID", "FK_Parent")]
    public abstract CaseParent Parent { get; }
}

[Table("Orders")]
public abstract partial class CaseOrder(IRowData row, IDataSourceAccess source)
    : Immutable<CaseOrder, CaseDb>(row, source), ITableModel<CaseDb>
{
    [PrimaryKey, Column("ID")] public abstract int Id { get; }
    [Column("Quantity"), Default(7)] public abstract int Quantity { get; }
}
