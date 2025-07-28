using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.MariaDB.information_schema;

public partial interface IMARIADBVIEWS
{
}

[Definition("")]
[View("VIEWS")]
[Interface<IMARIADBVIEWS>]
public abstract partial class VIEWS(IRowData rowData, IDataSourceAccess dataSource) : Immutable<VIEWS, MariaDBInformationSchema>(rowData, dataSource), IViewModel<MariaDBInformationSchema>
{
    [Type(DatabaseType.MariaDB, "varchar", 10)]
    [Column("ALGORITHM")]
    public abstract string ALGORITHM { get; }

    [Type(DatabaseType.MariaDB, "varchar", 32)]
    [Column("CHARACTER_SET_CLIENT")]
    public abstract string CHARACTER_SET_CLIENT { get; }

    [Type(DatabaseType.MariaDB, "varchar", 8)]
    [Column("CHECK_OPTION")]
    public abstract string CHECK_OPTION { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("COLLATION_CONNECTION")]
    public abstract string COLLATION_CONNECTION { get; }

    [Type(DatabaseType.MariaDB, "varchar", 384)]
    [Column("DEFINER")]
    public abstract string DEFINER { get; }

    [Type(DatabaseType.MariaDB, "varchar", 3)]
    [Column("IS_UPDATABLE")]
    public abstract string IS_UPDATABLE { get; }

    [Type(DatabaseType.MariaDB, "varchar", 7)]
    [Column("SECURITY_TYPE")]
    public abstract string SECURITY_TYPE { get; }

    [Type(DatabaseType.MariaDB, "varchar", 512)]
    [Column("TABLE_CATALOG")]
    public abstract string TABLE_CATALOG { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("TABLE_NAME")]
    public abstract string TABLE_NAME { get; }

    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("TABLE_SCHEMA")]
    public abstract string TABLE_SCHEMA { get; }

    [Type(DatabaseType.MariaDB, "longtext", 4294967295)]
    [Column("VIEW_DEFINITION")]
    public abstract string VIEW_DEFINITION { get; }

}