using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface TABLE_CONSTRAINTS : IViewModel
    {
        [Type("varchar", 512)]
        string CONSTRAINT_CATALOG { get; }

        [Type("varchar", 64)]
        string CONSTRAINT_NAME { get; }

        [Type("varchar", 64)]
        string CONSTRAINT_SCHEMA { get; }

        [Type("varchar", 64)]
        string CONSTRAINT_TYPE { get; }

        [Type("varchar", 64)]
        string TABLE_NAME { get; }

        [Type("varchar", 64)]
        string TABLE_SCHEMA { get; }

    }
}