using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("COLUMNS")]
    public interface COLUMNS : IViewModel
    {
        [Nullable]
        [Type("bigint")]
        long? CHARACTER_MAXIMUM_LENGTH { get; }

        [Nullable]
        [Type("bigint")]
        long? CHARACTER_OCTET_LENGTH { get; }

        [Nullable]
        [Type("varchar", 32)]
        string CHARACTER_SET_NAME { get; }

        [Nullable]
        [Type("varchar", 32)]
        string COLLATION_NAME { get; }

        [Type("varchar", 1024)]
        string COLUMN_COMMENT { get; }

        [Nullable]
        [Type("longtext", 4294967295)]
        string COLUMN_DEFAULT { get; }

        [Type("varchar", 3)]
        string COLUMN_KEY { get; }

        [Type("varchar", 64)]
        string COLUMN_NAME { get; }

        [Type("longtext", 4294967295)]
        string COLUMN_TYPE { get; }

        [Type("varchar", 64)]
        string DATA_TYPE { get; }

        [Nullable]
        [Type("bigint")]
        long? DATETIME_PRECISION { get; }

        [Type("varchar", 30)]
        string EXTRA { get; }

        [Nullable]
        [Type("longtext", 4294967295)]
        string GENERATION_EXPRESSION { get; }

        [Type("varchar", 6)]
        string IS_GENERATED { get; }

        [Type("varchar", 3)]
        string IS_NULLABLE { get; }

        [Nullable]
        [Type("bigint")]
        long? NUMERIC_PRECISION { get; }

        [Nullable]
        [Type("bigint")]
        long? NUMERIC_SCALE { get; }

        [Type("bigint")]
        long ORDINAL_POSITION { get; }

        [Type("varchar", 80)]
        string PRIVILEGES { get; }

        [Type("varchar", 512)]
        string TABLE_CATALOG { get; }

        [Type("varchar", 64)]
        string TABLE_NAME { get; }

        [Type("varchar", 64)]
        string TABLE_SCHEMA { get; }

    }
}