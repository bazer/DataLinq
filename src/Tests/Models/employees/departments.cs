using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface departments : ITableModel
    {
        string dept_name { get; }

        [PrimaryKey]
        Guid dept_no { get; }

    }
}