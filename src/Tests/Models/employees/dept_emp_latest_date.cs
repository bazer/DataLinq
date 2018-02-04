using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
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