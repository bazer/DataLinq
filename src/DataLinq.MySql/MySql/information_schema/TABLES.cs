using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.MySql.Shared;

namespace DataLinq.MySql.information_schema;

public partial interface IMYSQLTABLES
{
}

[Definition("")]
[View("TABLES")]
[Interface<IMYSQLTABLES>]
public abstract partial class TABLES(RowData rowData, DataSourceAccess dataSource) : Immutable<TABLES, MySQLInformationSchema>(rowData, dataSource), IViewModel<MySQLInformationSchema>
{
    public enum ROW_FORMATValue
    {
        Fixed = 1,
        Dynamic = 2,
        Compressed = 3,
        Redundant = 4,
        Compact = 5,
        Paged = 6,
    }
    
    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("AUTO_INCREMENT")]
    public abstract ulong? AUTO_INCREMENT { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("AVG_ROW_LENGTH")]
    public abstract ulong? AVG_ROW_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "datetime")]
    [Column("CHECK_TIME")]
    public abstract DateTime? CHECK_TIME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint")]
    [Column("CHECKSUM")]
    public abstract long? CHECKSUM { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 256)]
    [Column("CREATE_OPTIONS")]
    public abstract string? CREATE_OPTIONS { get; }

    [Type(DatabaseType.MySQL, "timestamp")]
    [Column("CREATE_TIME")]
    public abstract DateTime CREATE_TIME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("DATA_FREE")]
    public abstract ulong? DATA_FREE { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("DATA_LENGTH")]
    public abstract ulong? DATA_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("ENGINE")]
    public abstract string? ENGINE { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("INDEX_LENGTH")]
    public abstract ulong? INDEX_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("MAX_DATA_LENGTH")]
    public abstract ulong? MAX_DATA_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "enum")]
    [Enum("Fixed", "Dynamic", "Compressed", "Redundant", "Compact", "Paged")]
    [Column("ROW_FORMAT")]
    public abstract ROW_FORMATValue? ROW_FORMAT { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_CATALOG")]
    public abstract string? TABLE_CATALOG { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_COLLATION")]
    public abstract string? TABLE_COLLATION { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "text", 65535)]
    [Column("TABLE_COMMENT")]
    public abstract string? TABLE_COMMENT { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_NAME")]
    public abstract string? TABLE_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("TABLE_ROWS")]
    public abstract ulong? TABLE_ROWS { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_SCHEMA")]
    public abstract string? TABLE_SCHEMA { get; }

    [Type(DatabaseType.MySQL, "enum")]
    [Enum("BASE TABLE", "VIEW", "SYSTEM VIEW")]
    [Column("TABLE_TYPE")]
    public abstract TABLE_TYPE TABLE_TYPE { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "datetime")]
    [Column("UPDATE_TIME")]
    public abstract DateTime? UPDATE_TIME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "int")]
    [Column("VERSION")]
    public abstract int? VERSION { get; }

}