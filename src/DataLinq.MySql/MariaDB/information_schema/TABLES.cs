using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.MySql.Shared;

namespace DataLinq.MariaDB.information_schema;

public partial interface IMARIADBTABLES
{
}

[Definition("")]
[View("TABLES")]
[Interface<IMARIADBTABLES>]
public abstract partial class TABLES(RowData rowData, DataSourceAccess dataSource) : Immutable<TABLES, MariaDBInformationSchema>(rowData, dataSource), IViewModel<MariaDBInformationSchema>
{
    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("AUTO_INCREMENT")]
    public abstract ulong? AUTO_INCREMENT { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("AVG_ROW_LENGTH")]
    public abstract ulong? AVG_ROW_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "datetime")]
    [Column("CHECK_TIME")]
    public abstract DateTime? CHECK_TIME { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("CHECKSUM")]
    public abstract ulong? CHECKSUM { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "varchar", 2048)]
    [Column("CREATE_OPTIONS")]
    public abstract string? CREATE_OPTIONS { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "datetime")]
    [Column("CREATE_TIME")]
    public abstract DateTime? CREATE_TIME { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("DATA_FREE")]
    public abstract ulong? DATA_FREE { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("DATA_LENGTH")]
    public abstract ulong? DATA_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("ENGINE")]
    public abstract string? ENGINE { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("INDEX_LENGTH")]
    public abstract ulong? INDEX_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("MAX_DATA_LENGTH")]
    public abstract ulong? MAX_DATA_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("MAX_INDEX_LENGTH")]
    public abstract ulong? MAX_INDEX_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "varchar", 10)]
    [Column("ROW_FORMAT")]
    public abstract string? ROW_FORMAT { get; }

    [Type(DatabaseType.MariaDB, "varchar", 512)]
    [Column("TABLE_CATALOG")]
    public abstract string TABLE_CATALOG { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("TABLE_COLLATION")]
    public abstract string? TABLE_COLLATION { get; }

    [Type(DatabaseType.MariaDB, "varchar", 2048)]
    [Column("TABLE_COMMENT")]
    public abstract string TABLE_COMMENT { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("TABLE_NAME")]
    public abstract string TABLE_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("TABLE_ROWS")]
    public abstract ulong? TABLE_ROWS { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("TABLE_SCHEMA")]
    public abstract string TABLE_SCHEMA { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Enum("BASE TABLE", "VIEW", "SYSTEM VIEW")]
    [Column("TABLE_TYPE")]
    public abstract TABLE_TYPE TABLE_TYPE { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "varchar", 1)]
    [Column("TEMPORARY")]
    public abstract string? TEMPORARY { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "datetime")]
    [Column("UPDATE_TIME")]
    public abstract DateTime? UPDATE_TIME { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21, false)]
    [Column("VERSION")]
    public abstract ulong? VERSION { get; }

}