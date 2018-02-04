using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface STATISTICS : IViewModel
    {
        [Nullable]
        [Type("bigint")]
        long? CARDINALITY { get; }

        [Nullable]
        [Type("varchar", 1)]
        string COLLATION { get; }

        [Type("varchar", 64)]
        string COLUMN_NAME { get; }

        [Nullable]
        [Type("varchar", 16)]
        string COMMENT { get; }

        [Type("varchar", 1024)]
        string INDEX_COMMENT { get; }

        [Type("varchar", 64)]
        string INDEX_NAME { get; }

        [Type("varchar", 64)]
        string INDEX_SCHEMA { get; }

        [Type("varchar", 16)]
        string INDEX_TYPE { get; }

        [Type("bigint")]
        long NON_UNIQUE { get; }

        [Type("varchar", 3)]
        string NULLABLE { get; }

        [Nullable]
        [Type("varchar", 10)]
        string PACKED { get; }

        [Type("bigint")]
        long SEQ_IN_INDEX { get; }

        [Nullable]
        [Type("bigint")]
        long? SUB_PART { get; }

        [Type("varchar", 512)]
        string TABLE_CATALOG { get; }

        [Type("varchar", 64)]
        string TABLE_NAME { get; }

        [Type("varchar", 64)]
        string TABLE_SCHEMA { get; }

    }
}