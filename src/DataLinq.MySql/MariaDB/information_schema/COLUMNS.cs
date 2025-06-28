using System;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.MySql.Shared;

namespace DataLinq.MariaDB.information_schema;

public partial interface IMARIADBCOLUMNS: ICOLUMNS
{
}

[Definition("")]
[View("COLUMNS")]
[Interface<IMARIADBCOLUMNS>]
public abstract partial class COLUMNS(RowData rowData, DataSourceAccess dataSource) : Immutable<COLUMNS, MariaDBInformationSchema>(rowData, dataSource), IViewModel<MariaDBInformationSchema>
{
    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("CHARACTER_MAXIMUM_LENGTH")]
    public abstract ulong? CHARACTER_MAXIMUM_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("CHARACTER_OCTET_LENGTH")]
    public abstract ulong? CHARACTER_OCTET_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "varchar", 32)]
    [Column("CHARACTER_SET_NAME")]
    public abstract string? CHARACTER_SET_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("COLLATION_NAME")]
    public abstract string? COLLATION_NAME { get; }

    [Type(DatabaseType.MariaDB, "varchar", 1024)]
    [Column("COLUMN_COMMENT")]
    public abstract string COLUMN_COMMENT { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "longtext", 4294967295)]
    [Column("COLUMN_DEFAULT")]
    public abstract string? COLUMN_DEFAULT { get; }

    [Type(DatabaseType.MariaDB, "varchar", 3)]
    [Enum("", "PRI", "UNI", "MUL")]
    [Column("COLUMN_KEY")]
    public abstract COLUMN_KEYValue COLUMN_KEY { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("COLUMN_NAME")]
    public abstract string COLUMN_NAME { get; }

    [Type(DatabaseType.MariaDB, "longtext", 4294967295)]
    [Column("COLUMN_TYPE")]
    public abstract string COLUMN_TYPE { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("DATA_TYPE")]
    public abstract string DATA_TYPE { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("DATETIME_PRECISION")]
    public abstract ulong? DATETIME_PRECISION { get; }

    [Type(DatabaseType.MariaDB, "varchar", 80)]
    [Column("EXTRA")]
    public abstract string EXTRA { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "longtext", 4294967295)]
    [Column("GENERATION_EXPRESSION")]
    public abstract string? GENERATION_EXPRESSION { get; }

    [Type(DatabaseType.MariaDB, "varchar", 6)]
    [Column("IS_GENERATED")]
    public abstract string IS_GENERATED { get; }

    [Type(DatabaseType.MariaDB, "varchar", 3)]
    [Column("IS_NULLABLE")]
    public abstract string IS_NULLABLE { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("NUMERIC_PRECISION")]
    public abstract ulong? NUMERIC_PRECISION { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("NUMERIC_SCALE")]
    public abstract ulong? NUMERIC_SCALE { get; }

    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("ORDINAL_POSITION")]
    public abstract ulong ORDINAL_POSITION { get; }

    [Type(DatabaseType.MariaDB, "varchar", 80)]
    [Column("PRIVILEGES")]
    public abstract string PRIVILEGES { get; }

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