using System;
using System.Collections.Generic;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    [Name("current_dept_emp")]
    public partial class current_dept_emp : IViewModel
    {
        [Type("char", 4)]
        public virtual string dept_no { get; set; }

        [Type("int")]
        public virtual int emp_no { get; set; }

        [Nullable]
        [Type("date")]
        public virtual DateTime? from_date { get; set; }

        [Nullable]
        [Type("date")]
        public virtual DateTime? to_date { get; set; }

    }
}