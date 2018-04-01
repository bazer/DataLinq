using System;
using System.Collections.Generic;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("employees")]
    public interface employees : ITableModel
    {
        [PrimaryKey]
        [Type("int")]
        int emp_no { get; }

        [Relation("dept_emp", "emp_no")]
        IEnumerable<dept_emp> dept_emp { get; }

        [Relation("dept_manager", "emp_no")]
        IEnumerable<dept_manager> dept_manager { get; }

        [Relation("salaries", "emp_no")]
        IEnumerable<salaries> salaries { get; }

        [Relation("titles", "emp_no")]
        IEnumerable<titles> titles { get; }

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