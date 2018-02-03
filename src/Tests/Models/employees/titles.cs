using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface titles : ITableModel
    {
        [PrimaryKey]
        int emp_no { get; }

        [PrimaryKey]
        DateTime from_date { get; }

        [PrimaryKey]
        string title { get; }

        DateTime? to_date { get; }

    }
}