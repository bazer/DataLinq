using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.MySql.Models;

[Definition("")]
[View("KEY_COLUMN_USAGE")]
public abstract partial class KEY_COLUMN_USAGE(RowData rowData, DataSourceAccess dataSource) : Immutable<KEY_COLUMN_USAGE>(rowData, dataSource), IViewModel<information_schema>
{
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("COLUMN_NAME")]
    public abstract string? COLUMN_NAME { get; }

    [Type(DatabaseType.MySQL, "varchar", 512)]
    [Column("CONSTRAINT_CATALOG")]
    public abstract string? CONSTRAINT_CATALOG { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("CONSTRAINT_NAME")]
    public abstract string? CONSTRAINT_NAME { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("CONSTRAINT_SCHEMA")]
    public abstract string? CONSTRAINT_SCHEMA { get; }

    [Type(DatabaseType.MySQL, "bigint", 0)]
    [Column("ORDINAL_POSITION")]
    public abstract long? ORDINAL_POSITION { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", 0)]
    [Column("POSITION_IN_UNIQUE_CONSTRAINT")]
    public abstract long? POSITION_IN_UNIQUE_CONSTRAINT { get; }

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

    [Type(DatabaseType.MySQL, "varchar", 512)]
    [Column("TABLE_CATALOG")]
    public abstract string? TABLE_CATALOG { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_NAME")]
    public abstract string? TABLE_NAME { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_SCHEMA")]
    public abstract string? TABLE_SCHEMA { get; }

}