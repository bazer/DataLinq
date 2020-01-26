using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface users : ITableModel
    {
        int Age { get; }

        [PrimaryKey]
        int Id { get; }

        string Name { get; }

    }
}