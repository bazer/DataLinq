using System;
using System.Collections.Generic;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("salaries")]
    public interface salaries : ITableModel
    {
        [PrimaryKey]
        [ForeignKey("employees", "emp_no", "salaries_ibfk_1")]
        [Type("int")]
        int emp_no { get; }

        [Relation("employees", "emp_no")]
        employees employees { get; }

        [PrimaryKey]
        [Type("date")]
        DateTime from_date { get; }

        [Type("int")]
        int salary { get; }

        [Type("date")]
        DateTime to_date { get; }

    }
}