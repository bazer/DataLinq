using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface objectx : ITableModel
    {
        string Name { get; }

        string ObjectXId { get; }

    }
}