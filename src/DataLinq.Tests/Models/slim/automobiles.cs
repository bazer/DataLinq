using System;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    public interface automobiles : ITableModel
    {
        [PrimaryKey]
        int Id { get; }

        string Name { get; }

    }
}