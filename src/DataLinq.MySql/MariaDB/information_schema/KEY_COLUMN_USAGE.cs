using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.MariaDB.information_schema;

public partial interface IMARIADBKEY_COLUMN_USAGE
{
}

[Definition("")]
[View("KEY_COLUMN_USAGE")]
[Interface<IMARIADBKEY_COLUMN_USAGE>]
public abstract partial class KEY_COLUMN_USAGE(IRowData rowData, IDataSourceAccess dataSource) : Immutable<KEY_COLUMN_USAGE, MariaDBInformationSchema>(rowData, dataSource), IViewModel<MariaDBInformationSchema>
{
    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("COLUMN_NAME")]
    public abstract string COLUMN_NAME { get; }

    [Type(DatabaseType.MariaDB, "varchar", 512)]
    [Column("CONSTRAINT_CATALOG")]
    public abstract string CONSTRAINT_CATALOG { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("CONSTRAINT_NAME")]
    public abstract string CONSTRAINT_NAME { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("CONSTRAINT_SCHEMA")]
    public abstract string CONSTRAINT_SCHEMA { get; }

    [Type(DatabaseType.MariaDB, "bigint", 10)]
    [Column("ORDINAL_POSITION")]
    public abstract long ORDINAL_POSITION { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 10)]
    [Column("POSITION_IN_UNIQUE_CONSTRAINT")]
    public abstract long? POSITION_IN_UNIQUE_CONSTRAINT { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("REFERENCED_COLUMN_NAME")]
    public abstract string? REFERENCED_COLUMN_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("REFERENCED_TABLE_NAME")]
    public abstract string? REFERENCED_TABLE_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("REFERENCED_TABLE_SCHEMA")]
    public abstract string? REFERENCED_TABLE_SCHEMA { get; }

    [Type(DatabaseType.MariaDB, "varchar", 512)]
    [Column("TABLE_CATALOG")]
    public abstract string TABLE_CATALOG { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("TABLE_NAME")]
    public abstract string TABLE_NAME { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("TABLE_SCHEMA")]
    public abstract string TABLE_SCHEMA { get; }

}