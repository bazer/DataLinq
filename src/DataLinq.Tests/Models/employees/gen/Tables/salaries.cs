using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Table("salaries")]
    public partial record salaries : ITableModel
    {
        [PrimaryKey]
        [ForeignKey("employees", "emp_no", "salaries_ibfk_1")]
        [Type("int")]
        [Column("emp_no")]
        public virtual int emp_no { get; set; }

        [Relation("employees", "emp_no")]
        public virtual employees employees { get; }

        [PrimaryKey]
        [Type("date")]
        [Column("from_date")]
        public virtual DateOnly from_date { get; set; }

        [Type("int")]
        [Column("salary")]
        public virtual int salary { get; set; }

        [Type("date")]
        [Column("to_date")]
        public virtual DateOnly to_date { get; set; }

    }
}