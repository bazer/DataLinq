using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface people : ITableModel
    {
        [PrimaryKey]
        int Id { get; }

        string Name { get; }

    }
}