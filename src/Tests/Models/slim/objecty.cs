using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface objecty : ITableModel
    {
        string Name { get; }

        int ObjectYId { get; }

    }
}