using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.Tests.Models
{
    [Table("salaries")]
    public partial record salaries : ITableModel<Employees>
    {
        [PrimaryKey]
        [ForeignKey("employees", "emp_no", "salaries_ibfk_1")]
        [Type(DatabaseType.MySQL, "int")]
        [Type(DatabaseType.SQLite, "integer")]
        [Column("emp_no")]
        public virtual int emp_no { get; set; }

        [PrimaryKey]
        [Type(DatabaseType.MySQL, "date")]
        [Type(DatabaseType.SQLite, "text")]
        [Column("from_date")]
        public virtual DateOnly FromDate { get; set; }

        [Type(DatabaseType.MySQL, "int")]
        [Type(DatabaseType.SQLite, "integer")]
        [Column("salary")]
        public virtual int salary { get; set; }

        [Type(DatabaseType.MySQL, "date")]
        [Type(DatabaseType.SQLite, "text")]
        [Column("to_date")]
        public virtual DateOnly ToDate { get; set; }

        [Relation("employees", "emp_no")]
        public virtual Employee employees { get; }

    }
}