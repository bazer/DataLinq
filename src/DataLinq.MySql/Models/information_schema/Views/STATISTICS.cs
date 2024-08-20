using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.MySql.Models;

[Definition("")]
[View("STATISTICS")]
public abstract partial class STATISTICS(RowData rowData, DataSourceAccess dataSource) : Immutable<STATISTICS, information_schema>(rowData, dataSource), IViewModel<information_schema>
{
    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", 0)]
    [Column("CARDINALITY")]
    public abstract long? CARDINALITY { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 1)]
    [Column("COLLATION")]
    public abstract string? COLLATION { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("COLUMN_NAME")]
    public abstract string? COLUMN_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 16)]
    [Column("COMMENT")]
    public abstract string? COMMENT { get; }

    [Type(DatabaseType.MySQL, "varchar", 1024)]
    [Column("INDEX_COMMENT")]
    public abstract string? INDEX_COMMENT { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("INDEX_NAME")]
    public abstract string? INDEX_NAME { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("INDEX_SCHEMA")]
    public abstract string? INDEX_SCHEMA { get; }

    [Type(DatabaseType.MySQL, "varchar", 16)]
    [Column("INDEX_TYPE")]
    public abstract string? INDEX_TYPE { get; }

    [Type(DatabaseType.MySQL, "bigint", 0)]
    [Column("NON_UNIQUE")]
    public abstract long? NON_UNIQUE { get; }

    [Type(DatabaseType.MySQL, "varchar", 3)]
    [Column("NULLABLE")]
    public abstract string? NULLABLE { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 10)]
    [Column("PACKED")]
    public abstract string? PACKED { get; }

    [Type(DatabaseType.MySQL, "bigint", 0)]
    [Column("SEQ_IN_INDEX")]
    public abstract long? SEQ_IN_INDEX { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", 0)]
    [Column("SUB_PART")]
    public abstract long? SUB_PART { get; }

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