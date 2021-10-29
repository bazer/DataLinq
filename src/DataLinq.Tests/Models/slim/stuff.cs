using System;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace Tests.Models
{
    public interface stuff : ITableModel
    {
        DateTime? Created { get; }

        string Name { get; }

        [PrimaryKey]
        int TheId { get; }

    }
}