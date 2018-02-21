using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("departments")]
    public interface departments : ITableModel
    {
        [PrimaryKey]
        [ConstraintFrom("dept_emp", "dept_no", "dept_emp_ibfk_2")]
        [ConstraintFrom("dept_manager", "dept_no", "dept_manager_ibfk_2")]
        [Type("char", 4)]
        string dept_no { get; }

        [Type("varchar", 40)]
        string dept_name { get; }

    }
}