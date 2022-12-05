using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models
{
    [Table("salaries")]
    public partial record salaries : ITableModel
    {
        [PrimaryKey]
        [ForeignKey("employees", "emp_no", "salaries_ibfk_1")]
        [Type(DatabaseType.SQLite, "integer")]
        [Type(DatabaseType.MySQL, "int")]
        [Column("emp_no")]
        public virtual int emp_no { get; set; }

        [PrimaryKey]
        [Type(DatabaseType.SQLite, "text")]
        [Type(DatabaseType.MySQL, "date")]
        [Column("from_date")]
        public virtual DateOnly FromDate { get; set; }

        [Type(DatabaseType.SQLite, "integer")]
        [Type(DatabaseType.MySQL, "int")]
        [Column("salary")]
        public virtual int salary { get; set; }

        [Type(DatabaseType.SQLite, "text")]
        [Type(DatabaseType.MySQL, "date")]
        [Column("to_date")]
        public virtual DateOnly ToDate { get; set; }

        [Relation("employees", "emp_no")]
        public virtual Employee employees { get; }

    }
}