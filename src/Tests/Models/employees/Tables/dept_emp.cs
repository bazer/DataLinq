using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("dept_emp")]
    public interface dept_emp : ITableModel
    {
        [PrimaryKey]
        [ConstraintTo("departments", "dept_no", "dept_emp_ibfk_2")]
        [Type("char", 4)]
        string dept_no { get; }

        [PrimaryKey]
        [ConstraintTo("employees", "emp_no", "dept_emp_ibfk_1")]
        [Type("int")]
        int emp_no { get; }

        [Type("date")]
        DateTime from_date { get; }

        [Type("date")]
        DateTime to_date { get; }

    }
}