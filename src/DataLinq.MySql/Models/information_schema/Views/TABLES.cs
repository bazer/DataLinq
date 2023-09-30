using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.MySql.Models
{
    [Definition("")]
    [View("TABLES")]
    public partial record TABLES : IViewModel<information_schema>
    {
        [Nullable]
        [Type(DatabaseType.MySQL, "bigint", false)]
        [Column("AUTO_INCREMENT")]
        public virtual long? AUTO_INCREMENT { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "bigint", false)]
        [Column("AVG_ROW_LENGTH")]
        public virtual long? AVG_ROW_LENGTH { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "datetime")]
        [Column("CHECK_TIME")]
        public virtual DateTime? CHECK_TIME { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "bigint", false)]
        [Column("CHECKSUM")]
        public virtual long? CHECKSUM { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "varchar", 2048)]
        [Column("CREATE_OPTIONS")]
        public virtual string CREATE_OPTIONS { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "datetime")]
        [Column("CREATE_TIME")]
        public virtual DateTime? CREATE_TIME { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "bigint", false)]
        [Column("DATA_FREE")]
        public virtual long? DATA_FREE { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "bigint", false)]
        [Column("DATA_LENGTH")]
        public virtual long? DATA_LENGTH { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "varchar", 64)]
        [Column("ENGINE")]
        public virtual string ENGINE { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "bigint", false)]
        [Column("INDEX_LENGTH")]
        public virtual long? INDEX_LENGTH { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "bigint", false)]
        [Column("MAX_DATA_LENGTH")]
        public virtual long? MAX_DATA_LENGTH { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "bigint", false)]
        [Column("MAX_INDEX_LENGTH")]
        public virtual long? MAX_INDEX_LENGTH { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "varchar", 10)]
        [Column("ROW_FORMAT")]
        public virtual string ROW_FORMAT { get; set; }

        [Type(DatabaseType.MySQL, "varchar", 512)]
        [Column("TABLE_CATALOG")]
        public virtual string TABLE_CATALOG { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "varchar", 32)]
        [Column("TABLE_COLLATION")]
        public virtual string TABLE_COLLATION { get; set; }

        [Type(DatabaseType.MySQL, "varchar", 2048)]
        [Column("TABLE_COMMENT")]
        public virtual string TABLE_COMMENT { get; set; }

        [Type(DatabaseType.MySQL, "varchar", 64)]
        [Column("TABLE_NAME")]
        public virtual string TABLE_NAME { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "bigint", false)]
        [Column("TABLE_ROWS")]
        public virtual long? TABLE_ROWS { get; set; }

        [Type(DatabaseType.MySQL, "varchar", 64)]
        [Column("TABLE_SCHEMA")]
        public virtual string TABLE_SCHEMA { get; set; }

        [Type(DatabaseType.MySQL, "varchar", 64)]
        [Column("TABLE_TYPE")]
        public virtual string TABLE_TYPE { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "varchar", 1)]
        [Column("TEMPORARY")]
        public virtual string TEMPORARY { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "datetime")]
        [Column("UPDATE_TIME")]
        public virtual DateTime? UPDATE_TIME { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "bigint", false)]
        [Column("VERSION")]
        public virtual long? VERSION { get; set; }

    }
}