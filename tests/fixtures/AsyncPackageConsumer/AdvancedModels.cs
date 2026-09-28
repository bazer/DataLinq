using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace PackedGenerated;

public readonly record struct ChildId(int Value);
public sealed class ChildIdConverter : DataLinqScalarConverter<ChildId, int>
{
    public override int ToProvider(ChildId value, in ScalarConversionContext context) => value.Value;
    public override ChildId FromProvider(int value, in ScalarConversionContext context) => new(value);
}

[UseCache, Database("packed_generated")]
public partial class AdvancedDatabase(IDataLinqReadSource source) : IDatabaseModel
{
    public DbRead<Parent> Parents { get; } = new(source);
    public DbRead<Child> Children { get; } = new(source);
}

[Table("parents")]
public abstract partial class Parent(IRowData row, IDataLinqReadSource source)
    : Immutable<Parent, AdvancedDatabase>(row, source), ITableModel<AdvancedDatabase>
{
    [PrimaryKey, Column("id")] public abstract int Id { get; }
    [Relation("children", "parent_id", "FK_parent")] public abstract IImmutableRelation<Child> Children { get; }
    [Relation("children", "optional_parent_id", "FK_optional_parent")] public abstract IImmutableRelation<Child> OptionalChildren { get; }
}

[Table("children")]
public abstract partial class Child(IRowData row, IDataLinqReadSource source)
    : Immutable<Child, AdvancedDatabase>(row, source), ITableModel<AdvancedDatabase>
{
    [PrimaryKey, Column("tenant")] public abstract int Tenant { get; }
    [PrimaryKey, Column("id"), ScalarConverter(typeof(ChildIdConverter))] public abstract ChildId Id { get; }
    [Column("name")] public abstract string Name { get; }
    [ForeignKey("parents", "id", "FK_parent"), Column("parent_id")] public abstract int ParentId { get; }
    [ForeignKey("parents", "id", "FK_optional_parent"), Column("optional_parent_id"), Nullable] public abstract int? OptionalParentId { get; }
    [Relation("parents", "id", "FK_parent")] public abstract Parent Parent { get; }
    [Relation("parents", "id", "FK_optional_parent")] public abstract Parent? OptionalParent { get; }
}
