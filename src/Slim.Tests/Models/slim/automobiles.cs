using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface automobiles : ITableModel
    {
        [PrimaryKey]
        int Id { get; }

        string Name { get; }

    }
}