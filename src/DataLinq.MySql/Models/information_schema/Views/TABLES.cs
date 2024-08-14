using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.MySql.Models;

[Definition("")]
[View("TABLES")]
public abstract partial class TABLES(RowData rowData, DataSourceAccess dataSource) : Immutable<TABLES>(rowData, dataSource), IViewModel<information_schema>
{
    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("AUTO_INCREMENT")]
    public abstract long? AUTO_INCREMENT { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("AVG_ROW_LENGTH")]
    public abstract long? AVG_ROW_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "datetime")]
    [Column("CHECK_TIME")]
    public abstract DateTime? CHECK_TIME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("CHECKSUM")]
    public abstract long? CHECKSUM { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 2048)]
    [Column("CREATE_OPTIONS")]
    public abstract string CREATE_OPTIONS { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "datetime")]
    [Column("CREATE_TIME")]
    public abstract DateTime? CREATE_TIME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("DATA_FREE")]
    public abstract long? DATA_FREE { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("DATA_LENGTH")]
    public abstract long? DATA_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("ENGINE")]
    public abstract string ENGINE { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("INDEX_LENGTH")]
    public abstract long? INDEX_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("MAX_DATA_LENGTH")]
    public abstract long? MAX_DATA_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("MAX_INDEX_LENGTH")]
    public abstract long? MAX_INDEX_LENGTH { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 10)]
    [Column("ROW_FORMAT")]
    public abstract string ROW_FORMAT { get; }

    [Type(DatabaseType.MySQL, "varchar", 512)]
    [Column("TABLE_CATALOG")]
    public abstract string TABLE_CATALOG { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 32)]
    [Column("TABLE_COLLATION")]
    public abstract string TABLE_COLLATION { get; }

    [Type(DatabaseType.MySQL, "varchar", 2048)]
    [Column("TABLE_COMMENT")]
    public abstract string TABLE_COMMENT { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_NAME")]
    public abstract string TABLE_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("TABLE_ROWS")]
    public abstract long? TABLE_ROWS { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_SCHEMA")]
    public abstract string TABLE_SCHEMA { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_TYPE")]
    public abstract string TABLE_TYPE { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 1)]
    [Column("TEMPORARY")]
    public abstract string TEMPORARY { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "datetime")]
    [Column("UPDATE_TIME")]
    public abstract DateTime? UPDATE_TIME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("VERSION")]
    public abstract long? VERSION { get; }

}