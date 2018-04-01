using System;
using System.Collections.Generic;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("departments")]
    public interface departments : ITableModel
    {
        [PrimaryKey]
        [Type("char", 4)]
        string dept_no { get; }

        [Relation("dept_emp", "dept_no")]
        IEnumerable<dept_emp> dept_emp { get; }

        [Relation("dept_manager", "dept_no")]
        IEnumerable<dept_manager> dept_manager { get; }

        [Type("varchar", 40)]
        string dept_name { get; }

    }
}