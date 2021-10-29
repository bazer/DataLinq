using System;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace Tests.Models
{
    public interface objectx : ITableModel
    {
        string Name { get; }

        string ObjectXId { get; }

    }
}