using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface employees : ITableModel
    {
        DateTime birth_date { get; }

        [PrimaryKey]
        int emp_no { get; }

        string first_name { get; }

        int gender { get; }

        DateTime hire_date { get; }

        string last_name { get; }

    }
}