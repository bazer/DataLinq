using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Table("dept_manager")]
    public partial record Manager : ITableModel
    {
        [Relation("departments", "dept_no")]
        public virtual Department Department { get; }

        [PrimaryKey]
        [ForeignKey("departments", "dept_no", "dept_manager_ibfk_2")]
        [Type("char", 4)]
        [Column("dept_no")]
        public virtual string dept_no { get; set; }

        [PrimaryKey]
        [ForeignKey("employees", "emp_no", "dept_manager_ibfk_1")]
        [Type("int")]
        [Column("emp_no")]
        public virtual int emp_no { get; set; }

        [Relation("employees", "emp_no")]
        public virtual employees employees { get; }

        [Type("date")]
        [Column("from_date")]
        public virtual DateOnly from_date { get; set; }

        [Type("date")]
        [Column("to_date")]
        public virtual DateOnly to_date { get; set; }

    }
}