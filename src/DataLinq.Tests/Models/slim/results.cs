using System;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    public interface results : ITableModel
    {
        [PrimaryKey]
        int Id { get; }

        string Name { get; }

        int Order { get; }

    }
}