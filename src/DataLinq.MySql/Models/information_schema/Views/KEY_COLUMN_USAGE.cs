using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.MySql.Models
{
    [Definition("")]
    [View("KEY_COLUMN_USAGE")]
    public partial record KEY_COLUMN_USAGE : IViewModel<information_schema>
    {
        [Type(DatabaseType.MySQL, "varchar", 64)]
        [Column("COLUMN_NAME")]
        public virtual string COLUMN_NAME { get; set; }

        [Type(DatabaseType.MySQL, "varchar", 512)]
        [Column("CONSTRAINT_CATALOG")]
        public virtual string CONSTRAINT_CATALOG { get; set; }

        [Type(DatabaseType.MySQL, "varchar", 64)]
        [Column("CONSTRAINT_NAME")]
        public virtual string CONSTRAINT_NAME { get; set; }

        [Type(DatabaseType.MySQL, "varchar", 64)]
        [Column("CONSTRAINT_SCHEMA")]
        public virtual string CONSTRAINT_SCHEMA { get; set; }

        [Type(DatabaseType.MySQL, "bigint")]
        [Column("ORDINAL_POSITION")]
        public virtual long ORDINAL_POSITION { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "bigint")]
        [Column("POSITION_IN_UNIQUE_CONSTRAINT")]
        public virtual long? POSITION_IN_UNIQUE_CONSTRAINT { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "varchar", 64)]
        [Column("REFERENCED_COLUMN_NAME")]
        public virtual string REFERENCED_COLUMN_NAME { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "varchar", 64)]
        [Column("REFERENCED_TABLE_NAME")]
        public virtual string REFERENCED_TABLE_NAME { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "varchar", 64)]
        [Column("REFERENCED_TABLE_SCHEMA")]
        public virtual string REFERENCED_TABLE_SCHEMA { get; set; }

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
}