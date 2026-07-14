using System;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models.GeneratedDefaults;

[Database("generated_defaults")]
public partial class GeneratedDefaultDb(IDataLinqReadSource readSource) : IDatabaseModel
{
    public DbRead<GeneratedDefaultRow> Rows { get; } = new(readSource);
}

[Table("generated_default_rows")]
public abstract partial class GeneratedDefaultRow(IRowData rowData, IDataLinqReadSource readSource)
    : Immutable<GeneratedDefaultRow, GeneratedDefaultDb>(rowData, readSource),
      ITableModel<GeneratedDefaultDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "TEXT")]
    [GuidStorage(GuidStorageFormat.Text36)]
    [DefaultNewUUID(UUIDVersion.Version7)]
    [Column("version7_id")]
    public abstract Guid Version7Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [GuidStorage(GuidStorageFormat.Text36)]
    [DefaultNewUUID(UUIDVersion.Version4)]
    [Column("version4_id")]
    public abstract Guid Version4Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Column("name")]
    public abstract string Name { get; }
}
