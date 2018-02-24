using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("ROUTINES")]
    public interface ROUTINES : IViewModel
    {
        [Nullable]
        [Type("int")]
        int? CHARACTER_MAXIMUM_LENGTH { get; }

        [Nullable]
        [Type("int")]
        int? CHARACTER_OCTET_LENGTH { get; }

        [Type("varchar", 32)]
        string CHARACTER_SET_CLIENT { get; }

        [Nullable]
        [Type("varchar", 64)]
        string CHARACTER_SET_NAME { get; }

        [Type("varchar", 32)]
        string COLLATION_CONNECTION { get; }

        [Nullable]
        [Type("varchar", 64)]
        string COLLATION_NAME { get; }

        [Type("datetime")]
        DateTime CREATED { get; }

        [Type("varchar", 64)]
        string DATA_TYPE { get; }

        [Type("varchar", 32)]
        string DATABASE_COLLATION { get; }

        [Nullable]
        [Type("bigint")]
        long? DATETIME_PRECISION { get; }

        [Type("varchar", 189)]
        string DEFINER { get; }

        [Nullable]
        [Type("longtext", 4294967295)]
        string DTD_IDENTIFIER { get; }

        [Nullable]
        [Type("varchar", 64)]
        string EXTERNAL_LANGUAGE { get; }

        [Nullable]
        [Type("varchar", 64)]
        string EXTERNAL_NAME { get; }

        [Type("varchar", 3)]
        string IS_DETERMINISTIC { get; }

        [Type("datetime")]
        DateTime LAST_ALTERED { get; }

        [Nullable]
        [Type("int")]
        int? NUMERIC_PRECISION { get; }

        [Nullable]
        [Type("int")]
        int? NUMERIC_SCALE { get; }

        [Type("varchar", 8)]
        string PARAMETER_STYLE { get; }

        [Type("varchar", 8)]
        string ROUTINE_BODY { get; }

        [Type("varchar", 512)]
        string ROUTINE_CATALOG { get; }

        [Type("longtext", 4294967295)]
        string ROUTINE_COMMENT { get; }

        [Nullable]
        [Type("longtext", 4294967295)]
        string ROUTINE_DEFINITION { get; }

        [Type("varchar", 64)]
        string ROUTINE_NAME { get; }

        [Type("varchar", 64)]
        string ROUTINE_SCHEMA { get; }

        [Type("varchar", 9)]
        string ROUTINE_TYPE { get; }

        [Type("varchar", 7)]
        string SECURITY_TYPE { get; }

        [Type("varchar", 64)]
        string SPECIFIC_NAME { get; }

        [Type("varchar", 64)]
        string SQL_DATA_ACCESS { get; }

        [Type("varchar", 8192)]
        string SQL_MODE { get; }

        [Nullable]
        [Type("varchar", 64)]
        string SQL_PATH { get; }

    }
}