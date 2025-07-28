using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.MySql.information_schema;

public partial interface IMYSQLVIEWS
{
}

[Definition("")]
[View("VIEWS")]
[Interface<IMYSQLVIEWS>]
public abstract partial class VIEWS(IRowData rowData, IDataSourceAccess dataSource) : Immutable<VIEWS, MySQLInformationSchema>(rowData, dataSource), IViewModel<MySQLInformationSchema>
{
    public enum CHECK_OPTIONValue
    {
        NONE = 1,
        LOCAL = 2,
        CASCADED = 3,
    }
    
    public enum IS_UPDATABLEValue
    {
        NO = 1,
        YES = 2,
    }
    
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("CHARACTER_SET_CLIENT")]
    public abstract string CHARACTER_SET_CLIENT { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "enum")]
    [Enum("NONE", "LOCAL", "CASCADED")]
    [Column("CHECK_OPTION")]
    public abstract CHECK_OPTIONValue? CHECK_OPTION { get; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("COLLATION_CONNECTION")]
    public abstract string COLLATION_CONNECTION { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 288)]
    [Column("DEFINER")]
    public abstract string? DEFINER { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "enum")]
    [Enum("NO", "YES")]
    [Column("IS_UPDATABLE")]
    public abstract IS_UPDATABLEValue? IS_UPDATABLE { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 7)]
    [Column("SECURITY_TYPE")]
    public abstract string? SECURITY_TYPE { get; }

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

    [Nullable]
    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("VIEW_DEFINITION")]
    public abstract string? VIEW_DEFINITION { get; }

}