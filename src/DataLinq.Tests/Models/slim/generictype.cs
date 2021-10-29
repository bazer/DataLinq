using System;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace Tests.Models
{
    public interface generictype : ITableModel
    {
        string Id { get; }

        string Name { get; }

    }
}