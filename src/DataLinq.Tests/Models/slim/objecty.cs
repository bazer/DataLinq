using System;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    public interface objecty : ITableModel
    {
        string Name { get; }

        int ObjectYId { get; }

    }
}