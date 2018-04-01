using System;
using System.Collections.Generic;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("current_dept_emp")]
    public interface current_dept_emp : IViewModel
    {
        [Type("char", 4)]
        string dept_no { get; }

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