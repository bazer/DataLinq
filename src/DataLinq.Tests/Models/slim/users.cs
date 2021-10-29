using System;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace Tests.Models
{
    public interface users : ITableModel
    {
        int Age { get; }

        [PrimaryKey]
        int Id { get; }

        string Name { get; }

    }
}