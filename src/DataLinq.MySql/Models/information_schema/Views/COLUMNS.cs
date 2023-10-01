using System;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.MySql.Models;

[Definition("")]
[View("COLUMNS")]
public partial record COLUMNS : IViewModel<information_schema>
{
    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("CHARACTER_MAXIMUM_LENGTH")]
    public virtual long? CHARACTER_MAXIMUM_LENGTH { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("CHARACTER_OCTET_LENGTH")]
    public virtual long? CHARACTER_OCTET_LENGTH { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 32)]
    [Column("CHARACTER_SET_NAME")]
    public virtual string CHARACTER_SET_NAME { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "varchar", 32)]
    [Column("COLLATION_NAME")]
    public virtual string COLLATION_NAME { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 1024)]
    [Column("COLUMN_COMMENT")]
    public virtual string COLUMN_COMMENT { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("COLUMN_DEFAULT")]
    public virtual string COLUMN_DEFAULT { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 3)]
    [Column("COLUMN_KEY")]
    public virtual string COLUMN_KEY { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("COLUMN_NAME")]
    public virtual string COLUMN_NAME { get; set; }

    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("COLUMN_TYPE")]
    public virtual string COLUMN_TYPE { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Column("DATA_TYPE")]
    public virtual string DATA_TYPE { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("DATETIME_PRECISION")]
    public virtual long? DATETIME_PRECISION { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 30)]
    [Column("EXTRA")]
    public virtual string EXTRA { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "longtext", 4294967295)]
    [Column("GENERATION_EXPRESSION")]
    public virtual string GENERATION_EXPRESSION { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 6)]
    [Column("IS_GENERATED")]
    public virtual string IS_GENERATED { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 3)]
    [Column("IS_NULLABLE")]
    public virtual string IS_NULLABLE { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("NUMERIC_PRECISION")]
    public virtual long? NUMERIC_PRECISION { get; set; }

    [Nullable]
    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("NUMERIC_SCALE")]
    public virtual long? NUMERIC_SCALE { get; set; }

    [Type(DatabaseType.MySQL, "bigint", false)]
    [Column("ORDINAL_POSITION")]
    public virtual long ORDINAL_POSITION { get; set; }

    [Type(DatabaseType.MySQL, "varchar", 80)]
    [Column("PRIVILEGES")]
    public virtual string PRIVILEGES { get; set; }

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