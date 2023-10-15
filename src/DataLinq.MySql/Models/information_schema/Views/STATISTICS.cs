using System;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.MySql.Models;

[Definition("")]
[View("STATISTICS")]
public partial record STATISTICS : IViewModel<information_schema>
{
    [Nullable]
    [Type(DatabaseType.MySQL, "bigint")]
    [Column("CARDINALITY")]
    public virtual long? CARDINALITY { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 1)]
    [Column("COLLATION")]
    public virtual string COLLATION { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("COLUMN_NAME")]
    public virtual string COLUMN_NAME { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 16)]
    [Column("COMMENT")]
    public virtual string COMMENT { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 1024)]
    [Column("INDEX_COMMENT")]
    public virtual string INDEX_COMMENT { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("INDEX_NAME")]
    public virtual string INDEX_NAME { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("INDEX_SCHEMA")]
    public virtual string INDEX_SCHEMA { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 16)]
    [Column("INDEX_TYPE")]
    public virtual string INDEX_TYPE { get; set; }

    [Type(DatabaseType.MySQL, "bigint")]
    [Column("NON_UNIQUE")]
    public virtual long NON_UNIQUE { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 3)]
    [Column("NULLABLE")]
    public virtual string NULLABLE { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 10)]
    [Column("PACKED")]
    public virtual string PACKED { get; set; }

    [Type(DatabaseType.MySQL, "bigint")]
    [Column("SEQ_IN_INDEX")]
    public virtual long SEQ_IN_INDEX { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint")]
    [Column("SUB_PART")]
    public virtual long? SUB_PART { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 512)]
    [Column("TABLE_CATALOG")]
    public virtual string TABLE_CATALOG { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_NAME")]
    public virtual string TABLE_NAME { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("TABLE_SCHEMA")]
    public virtual string TABLE_SCHEMA { get; set; }

}