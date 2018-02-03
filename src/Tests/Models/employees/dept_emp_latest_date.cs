using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface dept_emp_latest_date : IViewModel
    {
        int emp_no { get; }

        DateTime? from_date { get; }

        DateTime? to_date { get; }

    }
}