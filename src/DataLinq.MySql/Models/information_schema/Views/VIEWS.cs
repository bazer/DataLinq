using System;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.MySql.Models;

[Definition("")]
[View("VIEWS")]
public partial record VIEWS : IViewModel<information_schema>
{
    [Type(DatabaseType.MySQL, "varchar", 10)]
    [Column("ALGORITHM")]
    public virtual string ALGORITHM { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 32)]
    [Column("CHARACTER_SET_CLIENT")]
    public virtual string CHARACTER_SET_CLIENT { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 8)]
    [Column("CHECK_OPTION")]
    public virtual string CHECK_OPTION { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 32)]
    [Column("COLLATION_CONNECTION")]
    public virtual string COLLATION_CONNECTION { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 189)]
    [Column("DEFINER")]
    public virtual string DEFINER { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 3)]
    [Column("IS_UPDATABLE")]
    public virtual string IS_UPDATABLE { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 7)]
    [Column("SECURITY_TYPE")]
    public virtual string SECURITY_TYPE { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 512)]
    [Column("TABLE_CATALOG")]
    public virtual string TABLE_CATALOG { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_NAME")]
    public virtual string TABLE_NAME { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_SCHEMA")]
    public virtual string TABLE_SCHEMA { get; set; }

    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("VIEW_DEFINITION")]
    public virtual string VIEW_DEFINITION { get; set; }

}