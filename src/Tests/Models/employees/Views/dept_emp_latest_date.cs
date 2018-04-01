using System;
using System.Collections.Generic;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("dept_emp_latest_date")]
    public interface dept_emp_latest_date : IViewModel
    {
        [Type("int")]
        int emp_no { get; }

        [Nullable]
        [Type("date")]
        DateTime? from_date { get; }

        [Nullable]
        [Type("date")]
        DateTime? to_date { get; }

    }
}