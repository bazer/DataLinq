using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Table("titles")]
    public partial record titles : ITableModel
    {
        [PrimaryKey]
        [ForeignKey("employees", "emp_no", "titles_ibfk_1")]
        [Type(DatabaseType.MySQL, "int")]
        [Column("emp_no")]
        public virtual int emp_no { get; set; }

        [PrimaryKey]
        [Type(DatabaseType.MySQL, "date")]
        [Column("from_date")]
        public virtual DateOnly from_date { get; set; }

        [PrimaryKey]
        [Type(DatabaseType.MySQL, "varchar", 50)]
        [Column("title")]
        public virtual string title { get; set; }

        [Nullable]
        [Type(DatabaseType.MySQL, "date")]
        [Column("to_date")]
        public virtual DateOnly? to_date { get; set; }

        [Relation("employees", "emp_no")]
        public virtual Employee employees { get; }

    }
}