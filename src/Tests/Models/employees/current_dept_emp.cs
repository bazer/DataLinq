using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface current_dept_emp : IViewModel
    {
        Guid dept_no { get; }

        int emp_no { get; }

        DateTime? from_date { get; }

        DateTime? to_date { get; }

    }
}