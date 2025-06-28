using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.MySql.Shared;

namespace DataLinq.MySql.information_schema;

public partial interface IMYSQLCOLUMNS: ICOLUMNS
{
}

[Definition("")]
[View("COLUMNS")]
[Interface<IMYSQLCOLUMNS>]
public abstract partial class COLUMNS(RowData rowData, DataSourceAccess dataSource) : Immutable<COLUMNS, MySQLInformationSchema>(rowData, dataSource), IViewModel<MySQLInformationSchema>
{
    [Nullable]
    [Type(DatabaseType.MySQL, "bigint")]
    [Column("CHARACTER_MAXIMUM_LENGTH")]
    public abstract ulong? CHARACTER_MAXIMUM_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint")]
    [Column("CHARACTER_OCTET_LENGTH")]
    public abstract long? CHARACTER_OCTET_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("CHARACTER_SET_NAME")]
    public abstract string? CHARACTER_SET_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("COLLATION_NAME")]
    public abstract string? COLLATION_NAME { get; }

    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("COLUMN_COMMENT")]
    public abstract string COLUMN_COMMENT { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("COLUMN_DEFAULT")]
    public abstract string? COLUMN_DEFAULT { get; }

    [Type(DatabaseType.MySQL, "enum")]
    [Enum("", "PRI", "UNI", "MUL")]
    [Column("COLUMN_KEY")]
    public abstract COLUMN_KEY COLUMN_KEY { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("COLUMN_NAME")]
    public abstract string? COLUMN_NAME { get; }

    [Type(DatabaseType.MySQL, "mediumtext", 16777215)]
    [Column("COLUMN_TYPE")]
    public abstract string COLUMN_TYPE { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("DATA_TYPE")]
    public abstract string? DATA_TYPE { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int", false)]
    [Column("DATETIME_PRECISION")]
    public abstract uint? DATETIME_PRECISION { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 256)]
    [Column("EXTRA")]
    public abstract string? EXTRA { get; }

    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("GENERATION_EXPRESSION")]
    public abstract string GENERATION_EXPRESSION { get; }

    [Type(DatabaseType.MySQL, "varchar", 3)]
    [Default("")]
    [Column("IS_NULLABLE")]
    public abstract string IS_NULLABLE { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("NUMERIC_PRECISION")]
    public abstract ulong? NUMERIC_PRECISION { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("NUMERIC_SCALE")]
    public abstract ulong? NUMERIC_SCALE { get; }

    [Type(DatabaseType.MySQL, "int", false)]
    [Column("ORDINAL_POSITION")]
    public abstract uint ORDINAL_POSITION { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 154)]
    [Column("PRIVILEGES")]
    public abstract string? PRIVILEGES { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int", false)]
    [Column("SRS_ID")]
    public abstract uint? SRS_ID { get; }

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