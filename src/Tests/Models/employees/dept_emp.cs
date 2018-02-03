using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface dept_emp : ITableModel
    {
        [PrimaryKey]
        Guid dept_no { get; }

        [PrimaryKey]
        int emp_no { get; }

        DateTime from_date { get; }

        DateTime to_date { get; }

    }
}