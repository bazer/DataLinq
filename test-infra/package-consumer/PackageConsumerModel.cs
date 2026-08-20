using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.PackageConsumer;

[UseCache]
[Database("package_consumer")]
public sealed partial class PackageConsumerDatabase(IDataLinqReadSource readSource)
    : IDatabaseModel
{
    public DbRead<PackageConsumerRow> Rows { get; } = new(readSource);
}

[Table("package_consumer_rows")]
public abstract partial class PackageConsumerRow(
    IRowData rowData,
    IDataLinqReadSource readSource)
    : Immutable<PackageConsumerRow, PackageConsumerDatabase>(rowData, readSource),
      ITableModel<PackageConsumerDatabase>
{
    [PrimaryKey]
    [Column("id")]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int")]
    [Type(DatabaseType.MariaDB, "int")]
    public abstract int Id { get; }

    [Column("group_id")]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int")]
    [Type(DatabaseType.MariaDB, "int")]
    public abstract int GroupId { get; }

    [Column("name")]
    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 100)]
    [Type(DatabaseType.MariaDB, "varchar", 100)]
    public abstract string Name { get; }

    [Column("external_guid")]
    [Type(DatabaseType.MariaDB, "uuid")]
    public abstract Guid ExternalGuid { get; }

    [Nullable]
    [Column("optional_external_guid")]
    [Type(DatabaseType.MariaDB, "uuid")]
    public abstract Guid? OptionalExternalGuid { get; }
}
