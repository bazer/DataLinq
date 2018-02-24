using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("KEY_COLUMN_USAGE")]
    public interface KEY_COLUMN_USAGE : IViewModel
    {
        [Type("varchar", 64)]
        string COLUMN_NAME { get; }

        [Type("varchar", 512)]
        string CONSTRAINT_CATALOG { get; }

        [Type("varchar", 64)]
        string CONSTRAINT_NAME { get; }

        [Type("varchar", 64)]
        string CONSTRAINT_SCHEMA { get; }

        [Type("bigint")]
        long ORDINAL_POSITION { get; }

        [Nullable]
        [Type("bigint")]
        long? POSITION_IN_UNIQUE_CONSTRAINT { get; }

        [Nullable]
        [Type("varchar", 64)]
        string REFERENCED_COLUMN_NAME { get; }

        [Nullable]
        [Type("varchar", 64)]
        string REFERENCED_TABLE_NAME { get; }

        [Nullable]
        [Type("varchar", 64)]
        string REFERENCED_TABLE_SCHEMA { get; }

        [Type("varchar", 512)]
        string TABLE_CATALOG { get; }

        [Type("varchar", 64)]
        string TABLE_NAME { get; }

        [Type("varchar", 64)]
        string TABLE_SCHEMA { get; }

    }
}