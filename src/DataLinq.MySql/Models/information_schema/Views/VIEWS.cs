using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.MySql.Models;

[Definition("")]
[View("VIEWS")]
public abstract partial class VIEWS(RowData rowData, DataSourceAccess dataSource) : Immutable<VIEWS, information_schema>(rowData, dataSource), IViewModel<information_schema>
{
    [Type(DatabaseType.MySQL, "varchar", 10)]
    [Column("ALGORITHM")]
    public abstract string ALGORITHM { get; }

    [Type(DatabaseType.MySQL, "varchar", 32)]
    [Column("CHARACTER_SET_CLIENT")]
    public abstract string CHARACTER_SET_CLIENT { get; }

    [Type(DatabaseType.MySQL, "varchar", 8)]
    [Column("CHECK_OPTION")]
    public abstract string CHECK_OPTION { get; }

    [Type(DatabaseType.MySQL, "varchar", 32)]
    [Column("COLLATION_CONNECTION")]
    public abstract string COLLATION_CONNECTION { get; }

    [Type(DatabaseType.MySQL, "varchar", 189)]
    [Column("DEFINER")]
    public abstract string DEFINER { get; }

    [Type(DatabaseType.MySQL, "varchar", 3)]
    [Column("IS_UPDATABLE")]
    public abstract string IS_UPDATABLE { get; }

    [Type(DatabaseType.MySQL, "varchar", 7)]
    [Column("SECURITY_TYPE")]
    public abstract string SECURITY_TYPE { get; }

    [Type(DatabaseType.MySQL, "varchar", 512)]
    [Column("TABLE_CATALOG")]
    public abstract string TABLE_CATALOG { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_NAME")]
    public abstract string TABLE_NAME { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_SCHEMA")]
    public abstract string TABLE_SCHEMA { get; }

    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("VIEW_DEFINITION")]
    public abstract string VIEW_DEFINITION { get; }

}