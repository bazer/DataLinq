using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("PARAMETERS")]
    public interface PARAMETERS : IViewModel
    {
        [Nullable]
        [Type("int")]
        int? CHARACTER_MAXIMUM_LENGTH { get; }

        [Nullable]
        [Type("int")]
        int? CHARACTER_OCTET_LENGTH { get; }

        [Nullable]
        [Type("varchar", 64)]
        string CHARACTER_SET_NAME { get; }

        [Nullable]
        [Type("varchar", 64)]
        string COLLATION_NAME { get; }

        [Type("varchar", 64)]
        string DATA_TYPE { get; }

        [Nullable]
        [Type("bigint")]
        long? DATETIME_PRECISION { get; }

        [Type("longtext", 4294967295)]
        string DTD_IDENTIFIER { get; }

        [Nullable]
        [Type("int")]
        int? NUMERIC_PRECISION { get; }

        [Nullable]
        [Type("int")]
        int? NUMERIC_SCALE { get; }

        [Type("int")]
        int ORDINAL_POSITION { get; }

        [Nullable]
        [Type("varchar", 5)]
        string PARAMETER_MODE { get; }

        [Nullable]
        [Type("varchar", 64)]
        string PARAMETER_NAME { get; }

        [Type("varchar", 9)]
        string ROUTINE_TYPE { get; }

        [Type("varchar", 512)]
        string SPECIFIC_CATALOG { get; }

        [Type("varchar", 64)]
        string SPECIFIC_NAME { get; }

        [Type("varchar", 64)]
        string SPECIFIC_SCHEMA { get; }

    }
}