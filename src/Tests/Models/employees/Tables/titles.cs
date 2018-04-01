using System;
using System.Collections.Generic;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("titles")]
    public interface titles : ITableModel
    {
        [PrimaryKey]
        [ForeignKey("employees", "emp_no", "titles_ibfk_1")]
        [Type("int")]
        int emp_no { get; }

        [Relation("employees", "emp_no")]
        employees employees { get; }

        [PrimaryKey]
        [Type("date")]
        DateTime from_date { get; }

        [PrimaryKey]
        [Type("varchar", 50)]
        string title { get; }

        [Nullable]
        [Type("date")]
        DateTime? to_date { get; }

    }
}