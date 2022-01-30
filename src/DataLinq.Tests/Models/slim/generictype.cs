using System;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    public interface generictype : ITableModel
    {
        string Id { get; }

        string Name { get; }

    }
}