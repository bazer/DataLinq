using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("dept_manager")]
    public interface dept_manager : ITableModel
    {
        [PrimaryKey]
        [ConstraintTo("departments", "dept_no", "dept_manager_ibfk_2")]
        [Type("char", 4)]
        Guid dept_no { get; }

        [PrimaryKey]
        [ConstraintTo("employees", "emp_no", "dept_manager_ibfk_1")]
        [Type("int")]
        int emp_no { get; }

        [Type("date")]
        DateTime from_date { get; }

        [Type("date")]
        DateTime to_date { get; }

    }
}