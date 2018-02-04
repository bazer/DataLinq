using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface FILES : IViewModel
    {
        [Nullable]
        [Type("bigint")]
        long? AUTOEXTEND_SIZE { get; }

        [Nullable]
        [Type("bigint")]
        long? AVG_ROW_LENGTH { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? CHECK_TIME { get; }

        [Nullable]
        [Type("bigint")]
        long? CHECKSUM { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? CREATE_TIME { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? CREATION_TIME { get; }

        [Nullable]
        [Type("bigint")]
        long? DATA_FREE { get; }

        [Nullable]
        [Type("bigint")]
        long? DATA_LENGTH { get; }

        [Nullable]
        [Type("bigint")]
        long? DELETED_ROWS { get; }

        [Type("varchar", 64)]
        string ENGINE { get; }

        [Type("bigint")]
        long EXTENT_SIZE { get; }

        [Nullable]
        [Type("varchar", 255)]
        string EXTRA { get; }

        [Type("bigint")]
        long FILE_ID { get; }

        [Nullable]
        [Type("varchar", 512)]
        string FILE_NAME { get; }

        [Type("varchar", 20)]
        string FILE_TYPE { get; }

        [Nullable]
        [Type("bigint")]
        long? FREE_EXTENTS { get; }

        [Nullable]
        [Type("varchar", 64)]
        string FULLTEXT_KEYS { get; }

        [Nullable]
        [Type("bigint")]
        long? INDEX_LENGTH { get; }

        [Nullable]
        [Type("bigint")]
        long? INITIAL_SIZE { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? LAST_ACCESS_TIME { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? LAST_UPDATE_TIME { get; }

        [Nullable]
        [Type("varchar", 64)]
        string LOGFILE_GROUP_NAME { get; }

        [Nullable]
        [Type("bigint")]
        long? LOGFILE_GROUP_NUMBER { get; }

        [Nullable]
        [Type("bigint")]
        long? MAX_DATA_LENGTH { get; }

        [Nullable]
        [Type("bigint")]
        long? MAXIMUM_SIZE { get; }

        [Nullable]
        [Type("bigint")]
        long? RECOVER_TIME { get; }

        [Nullable]
        [Type("varchar", 10)]
        string ROW_FORMAT { get; }

        [Type("varchar", 20)]
        string STATUS { get; }

        [Type("varchar", 64)]
        string TABLE_CATALOG { get; }

        [Nullable]
        [Type("varchar", 64)]
        string TABLE_NAME { get; }

        [Nullable]
        [Type("bigint")]
        long? TABLE_ROWS { get; }

        [Nullable]
        [Type("varchar", 64)]
        string TABLE_SCHEMA { get; }

        [Nullable]
        [Type("varchar", 64)]
        string TABLESPACE_NAME { get; }

        [Nullable]
        [Type("bigint")]
        long? TOTAL_EXTENTS { get; }

        [Nullable]
        [Type("bigint")]
        long? TRANSACTION_COUNTER { get; }

        [Nullable]
        [Type("bigint")]
        long? UPDATE_COUNT { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? UPDATE_TIME { get; }

        [Nullable]
        [Type("bigint")]
        long? VERSION { get; }

    }
}