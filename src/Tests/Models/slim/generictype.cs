using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface generictype : ITableModel
    {
        string Id { get; }

        string Name { get; }

    }
}