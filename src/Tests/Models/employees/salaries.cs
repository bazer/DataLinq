using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface salaries : ITableModel
    {
        [PrimaryKey]
        int emp_no { get; }

        [PrimaryKey]
        DateTime from_date { get; }

        int salary { get; }

        DateTime to_date { get; }

    }
}