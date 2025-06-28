using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.MariaDB.information_schema;

public partial interface IMARIADBSTATISTICS
{
}

[Definition("")]
[View("STATISTICS")]
[Interface<IMARIADBSTATISTICS>]
public abstract partial class STATISTICS(RowData rowData, DataSourceAccess dataSource) : Immutable<STATISTICS, MariaDBInformationSchema>(rowData, dataSource), IViewModel<MariaDBInformationSchema>
{
    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 21)]
    [Column("CARDINALITY")]
    public abstract long? CARDINALITY { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "varchar", 1)]
    [Column("COLLATION")]
    public abstract string? COLLATION { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("COLUMN_NAME")]
    public abstract string COLUMN_NAME { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "varchar", 16)]
    [Column("COMMENT")]
    public abstract string? COMMENT { get; }

    [Type(DatabaseType.MariaDB, "varchar", 3)]
    [Column("IGNORED")]
    public abstract string IGNORED { get; }

    [Type(DatabaseType.MariaDB, "varchar", 1024)]
    [Column("INDEX_COMMENT")]
    public abstract string INDEX_COMMENT { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("INDEX_NAME")]
    public abstract string INDEX_NAME { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("INDEX_SCHEMA")]
    public abstract string INDEX_SCHEMA { get; }

    [Type(DatabaseType.MariaDB, "varchar", 16)]
    [Column("INDEX_TYPE")]
    public abstract string INDEX_TYPE { get; }

    [Type(DatabaseType.MariaDB, "bigint", 1)]
    [Column("NON_UNIQUE")]
    public abstract long NON_UNIQUE { get; }

    [Type(DatabaseType.MariaDB, "varchar", 3)]
    [Column("NULLABLE")]
    public abstract string NULLABLE { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "varchar", 10)]
    [Column("PACKED")]
    public abstract string? PACKED { get; }

    [Type(DatabaseType.MariaDB, "int", 2, false)]
    [Column("SEQ_IN_INDEX")]
    public abstract uint SEQ_IN_INDEX { get; }

    [Nullable]
    [Type(DatabaseType.MariaDB, "bigint", 3)]
    [Column("SUB_PART")]
    public abstract long? SUB_PART { get; }

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