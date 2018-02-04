using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface PARTITIONS : IViewModel
    {
        [Type("bigint")]
        long AVG_ROW_LENGTH { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? CHECK_TIME { get; }

        [Nullable]
        [Type("bigint")]
        long? CHECKSUM { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? CREATE_TIME { get; }

        [Type("bigint")]
        long DATA_FREE { get; }

        [Type("bigint")]
        long DATA_LENGTH { get; }

        [Type("bigint")]
        long INDEX_LENGTH { get; }

        [Nullable]
        [Type("bigint")]
        long? MAX_DATA_LENGTH { get; }

        [Type("varchar", 12)]
        string NODEGROUP { get; }

        [Type("varchar", 80)]
        string PARTITION_COMMENT { get; }

        [Nullable]
        [Type("longtext", 4294967295)]
        string PARTITION_DESCRIPTION { get; }

        [Nullable]
        [Type("longtext", 4294967295)]
        string PARTITION_EXPRESSION { get; }

        [Nullable]
        [Type("varchar", 18)]
        string PARTITION_METHOD { get; }

        [Nullable]
        [Type("varchar", 64)]
        string PARTITION_NAME { get; }

        [Nullable]
        [Type("bigint")]
        long? PARTITION_ORDINAL_POSITION { get; }

        [Nullable]
        [Type("longtext", 4294967295)]
        string SUBPARTITION_EXPRESSION { get; }

        [Nullable]
        [Type("varchar", 12)]
        string SUBPARTITION_METHOD { get; }

        [Nullable]
        [Type("varchar", 64)]
        string SUBPARTITION_NAME { get; }

        [Nullable]
        [Type("bigint")]
        long? SUBPARTITION_ORDINAL_POSITION { get; }

        [Type("varchar", 512)]
        string TABLE_CATALOG { get; }

        [Type("varchar", 64)]
        string TABLE_NAME { get; }

        [Type("bigint")]
        long TABLE_ROWS { get; }

        [Type("varchar", 64)]
        string TABLE_SCHEMA { get; }

        [Nullable]
        [Type("varchar", 64)]
        string TABLESPACE_NAME { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? UPDATE_TIME { get; }

    }
}