using System;
using Slim.Interfaces;
using Slim.Attributes;

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