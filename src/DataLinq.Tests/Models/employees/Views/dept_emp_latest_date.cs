using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Table("dept_emp_latest_date")]
    public partial record dept_emp_latest_date : IViewModel
    {
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