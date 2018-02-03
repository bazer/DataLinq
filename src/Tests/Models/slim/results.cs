using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface results : ITableModel
    {
        [PrimaryKey]
        int Id { get; }

        string Name { get; }

        int Order { get; }

    }
}