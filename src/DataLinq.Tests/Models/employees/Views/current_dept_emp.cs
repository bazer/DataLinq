using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Table("current_dept_emp")]
    public partial record current_dept_emp : IViewModel
    {
        [Type("char", 4)]
        [Column("dept_no")]
        public virtual string dept_no { get; set; }

        [Type("int")]
        [Column("emp_no")]
        public virtual int emp_no { get; set; }

        [Nullable]
        [Type("date")]
        [Column("from_date")]
        public virtual DateOnly? from_date { get; set; }

        [Nullable]
        [Type("date")]
        [Column("to_date")]
        public virtual DateOnly? to_date { get; set; }

    }
}