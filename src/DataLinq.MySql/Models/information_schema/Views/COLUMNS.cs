using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.MySql.Models;

[Definition("")]
[View("COLUMNS")]
public abstract partial class COLUMNS(RowData rowData, DataSourceAccess dataSource) : Immutable<COLUMNS, information_schema>(rowData, dataSource), IViewModel<information_schema>
{
    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", 0, false)]
    [Column("CHARACTER_MAXIMUM_LENGTH")]
    public abstract long? CHARACTER_MAXIMUM_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", 0, false)]
    [Column("CHARACTER_OCTET_LENGTH")]
    public abstract long? CHARACTER_OCTET_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 32)]
    [Column("CHARACTER_SET_NAME")]
    public abstract string? CHARACTER_SET_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 32)]
    [Column("COLLATION_NAME")]
    public abstract string? COLLATION_NAME { get; }

    [Type(DatabaseType.MySQL, "varchar", 1024)]
    [Column("COLUMN_COMMENT")]
    public abstract string COLUMN_COMMENT { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("COLUMN_DEFAULT")]
    public abstract string? COLUMN_DEFAULT { get; }

    [Type(DatabaseType.MySQL, "varchar", 3)]
    [Column("COLUMN_KEY")]
    public abstract string COLUMN_KEY { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("COLUMN_NAME")]
    public abstract string COLUMN_NAME { get; }

    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("COLUMN_TYPE")]
    public abstract string COLUMN_TYPE { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("DATA_TYPE")]
    public abstract string DATA_TYPE { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", 0, false)]
    [Column("DATETIME_PRECISION")]
    public abstract long? DATETIME_PRECISION { get; }

    [Type(DatabaseType.MySQL, "varchar", 80)]
    [Column("EXTRA")]
    public abstract string EXTRA { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("GENERATION_EXPRESSION")]
    public abstract string? GENERATION_EXPRESSION { get; }

    [Type(DatabaseType.MySQL, "varchar", 6)]
    [Column("IS_GENERATED")]
    public abstract string IS_GENERATED { get; }

    [Type(DatabaseType.MySQL, "varchar", 3)]
    [Column("IS_NULLABLE")]
    public abstract string IS_NULLABLE { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", 0, false)]
    [Column("NUMERIC_PRECISION")]
    public abstract long? NUMERIC_PRECISION { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", 0, false)]
    [Column("NUMERIC_SCALE")]
    public abstract long? NUMERIC_SCALE { get; }

    [Type(DatabaseType.MySQL, "bigint", 0, false)]
    [Column("ORDINAL_POSITION")]
    public abstract long ORDINAL_POSITION { get; }

    [Type(DatabaseType.MySQL, "varchar", 80)]
    [Column("PRIVILEGES")]
    public abstract string PRIVILEGES { get; }

    [Type(DatabaseType.MySQL, "varchar", 512)]
    [Column("TABLE_CATALOG")]
    public abstract string TABLE_CATALOG { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_NAME")]
    public abstract string TABLE_NAME { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_SCHEMA")]
    public abstract string TABLE_SCHEMA { get; }

}