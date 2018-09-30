using System;
using System.Collections.Generic;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("dept_emp_latest_date")]
    public partial class dept_emp_latest_date : IViewModel
    {
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