using System;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    public interface users : ITableModel
    {
        int Age { get; }

        [PrimaryKey]
        int Id { get; }

        string Name { get; }

    }
}