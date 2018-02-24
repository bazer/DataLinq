using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("TABLES")]
    public interface TABLES : IViewModel
    {
        [Nullable]
        [Type("bigint")]
        long? AUTO_INCREMENT { get; }

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
        [Type("varchar", 2048)]
        string CREATE_OPTIONS { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? CREATE_TIME { get; }

        [Nullable]
        [Type("bigint")]
        long? DATA_FREE { get; }

        [Nullable]
        [Type("bigint")]
        long? DATA_LENGTH { get; }

        [Nullable]
        [Type("varchar", 64)]
        string ENGINE { get; }

        [Nullable]
        [Type("bigint")]
        long? INDEX_LENGTH { get; }

        [Nullable]
        [Type("bigint")]
        long? MAX_DATA_LENGTH { get; }

        [Nullable]
        [Type("varchar", 10)]
        string ROW_FORMAT { get; }

        [Type("varchar", 512)]
        string TABLE_CATALOG { get; }

        [Nullable]
        [Type("varchar", 32)]
        string TABLE_COLLATION { get; }

        [Type("varchar", 2048)]
        string TABLE_COMMENT { get; }

        [Type("varchar", 64)]
        string TABLE_NAME { get; }

        [Nullable]
        [Type("bigint")]
        long? TABLE_ROWS { get; }

        [Type("varchar", 64)]
        string TABLE_SCHEMA { get; }

        [Type("varchar", 64)]
        string TABLE_TYPE { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? UPDATE_TIME { get; }

        [Nullable]
        [Type("bigint")]
        long? VERSION { get; }

    }
}