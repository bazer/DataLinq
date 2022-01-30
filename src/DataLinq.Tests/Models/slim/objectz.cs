using System;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.Tests.Models
{
    public interface objectz : ITableModel
    {
        int Id { get; }

        string Name { get; }

    }
}