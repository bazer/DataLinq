using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.MySql.information_schema;

public partial interface IMYSQLKEY_COLUMN_USAGE
{
}

[Definition("")]
[View("KEY_COLUMN_USAGE")]
[Interface<IMYSQLKEY_COLUMN_USAGE>]
public abstract partial class KEY_COLUMN_USAGE(IRowData rowData, IDataSourceAccess dataSource) : Immutable<KEY_COLUMN_USAGE, MySQLInformationSchema>(rowData, dataSource), IViewModel<MySQLInformationSchema>
{
    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("COLUMN_NAME")]
    public abstract string? COLUMN_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("CONSTRAINT_CATALOG")]
    public abstract string? CONSTRAINT_CATALOG { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("CONSTRAINT_NAME")]
    public abstract string? CONSTRAINT_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("CONSTRAINT_SCHEMA")]
    public abstract string? CONSTRAINT_SCHEMA { get; }

    [Type(DatabaseType.MySQL, "int", false)]
    [Default(0)]
    [Column("ORDINAL_POSITION")]
    public abstract uint ORDINAL_POSITION { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int", false)]
    [Column("POSITION_IN_UNIQUE_CONSTRAINT")]
    public abstract uint? POSITION_IN_UNIQUE_CONSTRAINT { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("REFERENCED_COLUMN_NAME")]
    public abstract string? REFERENCED_COLUMN_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("REFERENCED_TABLE_NAME")]
    public abstract string? REFERENCED_TABLE_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("REFERENCED_TABLE_SCHEMA")]
    public abstract string? REFERENCED_TABLE_SCHEMA { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_CATALOG")]
    public abstract string? TABLE_CATALOG { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_NAME")]
    public abstract string? TABLE_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_SCHEMA")]
    public abstract string? TABLE_SCHEMA { get; }

}