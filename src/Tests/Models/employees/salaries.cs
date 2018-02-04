using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface salaries : ITableModel
    {
        [PrimaryKey]
        [ConstraintTo("employees", "emp_no", "salaries_ibfk_1")]
        [Type("int")]
        int emp_no { get; }

        [PrimaryKey]
        [Type("date")]
        DateTime from_date { get; }

        [Type("int")]
        int salary { get; }

        [Type("date")]
        DateTime to_date { get; }

    }
}