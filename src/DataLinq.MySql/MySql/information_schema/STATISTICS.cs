using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.MySql.information_schema;

public partial interface IMYSQLSTATISTICS
{
}

[Definition("")]
[View("STATISTICS")]
[Interface<IMYSQLSTATISTICS>]
public abstract partial class STATISTICS(RowData rowData, DataSourceAccess dataSource) : Immutable<STATISTICS, MySQLInformationSchema>(rowData, dataSource), IViewModel<MySQLInformationSchema>
{
    [Nullable]
    [Type(DatabaseType.MySQL, "bigint")]
    [Column("CARDINALITY")]
    public abstract long? CARDINALITY { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 1)]
    [Column("COLLATION")]
    public abstract string? COLLATION { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("COLUMN_NAME")]
    public abstract string? COLUMN_NAME { get; }

    [Type(DatabaseType.MySQL, "varchar", 8)]
    [Default("")]
    [Column("COMMENT")]
    public abstract string COMMENT { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("EXPRESSION")]
    public abstract string? EXPRESSION { get; }

    [Type(DatabaseType.MySQL, "varchar", 2048)]
    [Column("INDEX_COMMENT")]
    public abstract string INDEX_COMMENT { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("INDEX_NAME")]
    public abstract string? INDEX_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("INDEX_SCHEMA")]
    public abstract string? INDEX_SCHEMA { get; }

    [Type(DatabaseType.MySQL, "varchar", 11)]
    [Default("")]
    [Column("INDEX_TYPE")]
    public abstract string INDEX_TYPE { get; }

    [Type(DatabaseType.MySQL, "varchar", 3)]
    [Default("")]
    [Column("IS_VISIBLE")]
    public abstract string IS_VISIBLE { get; }

    [Type(DatabaseType.MySQL, "int")]
    [Default(0)]
    [Column("NON_UNIQUE")]
    public abstract int NON_UNIQUE { get; }

    [Type(DatabaseType.MySQL, "varchar", 3)]
    [Default("")]
    [Column("NULLABLE")]
    public abstract string NULLABLE { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varbinary")]
    [Column("PACKED")]
    public abstract byte[]? PACKED { get; }

    [Type(DatabaseType.MySQL, "int", false)]
    [Column("SEQ_IN_INDEX")]
    public abstract uint SEQ_IN_INDEX { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint")]
    [Column("SUB_PART")]
    public abstract long? SUB_PART { get; }

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