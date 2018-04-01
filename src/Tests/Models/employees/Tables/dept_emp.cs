using System;
using System.Collections.Generic;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("dept_emp")]
    public interface dept_emp : ITableModel
    {
        [PrimaryKey]
        [ForeignKey("departments", "dept_no", "dept_emp_ibfk_2")]
        [Type("char", 4)]
        string dept_no { get; }

        [Relation("departments", "dept_no")]
        departments departments { get; }

        [PrimaryKey]
        [ForeignKey("employees", "emp_no", "dept_emp_ibfk_1")]
        [Type("int")]
        int emp_no { get; }

        [Relation("employees", "emp_no")]
        employees employees { get; }

        [Type("date")]
        DateTime from_date { get; }

        [Type("date")]
        DateTime to_date { get; }

    }
}