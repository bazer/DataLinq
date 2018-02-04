using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface employees : ITableModel
    {
        [PrimaryKey]
        [ConstraintFrom("dept_emp", "emp_no", "dept_emp_ibfk_1")]
        [ConstraintFrom("dept_manager", "emp_no", "dept_manager_ibfk_1")]
        [ConstraintFrom("salaries", "emp_no", "salaries_ibfk_1")]
        [ConstraintFrom("titles", "emp_no", "titles_ibfk_1")]
        [Type("int")]
        int emp_no { get; }

        [Type("date")]
        DateTime birth_date { get; }

        [Type("varchar", 14)]
        string first_name { get; }

        [Type("enum", 1)]
        int gender { get; }

        [Type("date")]
        DateTime hire_date { get; }

        [Type("varchar", 16)]
        string last_name { get; }

    }
}