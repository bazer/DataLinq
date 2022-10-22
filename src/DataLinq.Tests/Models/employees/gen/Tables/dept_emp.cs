using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Table("dept_emp")]
    public partial record dept_emp : ITableModel
    {
        [PrimaryKey]
        [ForeignKey("departments", "dept_no", "dept_emp_ibfk_2")]
        [Type(DatabaseType.MySQL, "char", 4)]
        [Column("dept_no")]
        public virtual string dept_no { get; set; }

        [PrimaryKey]
        [ForeignKey("employees", "emp_no", "dept_emp_ibfk_1")]
        [Type(DatabaseType.MySQL, "int")]
        [Column("emp_no")]
        public virtual int emp_no { get; set; }

        [Type(DatabaseType.MySQL, "date")]
        [Column("from_date")]
        public virtual DateOnly from_date { get; set; }

        [Type(DatabaseType.MySQL, "date")]
        [Column("to_date")]
        public virtual DateOnly to_date { get; set; }

        [Relation("departments", "dept_no")]
        public virtual Department departments { get; }

        [Relation("employees", "emp_no")]
        public virtual employees employees { get; }

    }
}