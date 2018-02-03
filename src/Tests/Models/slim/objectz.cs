using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface objectz : ITableModel
    {
        int Id { get; }

        string Name { get; }

    }
}