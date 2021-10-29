using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("SYSTEM_VARIABLES")]
    public interface SYSTEM_VARIABLES : IViewModel
    {
        [Nullable]
        [Type("varchar", 64)]
        string COMMAND_LINE_ARGUMENT { get; }

        [Nullable]
        [Type("varchar", 2048)]
        string DEFAULT_VALUE { get; }

        [Nullable]
        [Type("longtext", 4294967295)]
        string ENUM_VALUE_LIST { get; }

        [Nullable]
        [Type("varchar", 2048)]
        string GLOBAL_VALUE { get; }

        [Type("varchar", 64)]
        string GLOBAL_VALUE_ORIGIN { get; }

        [Nullable]
        [Type("varchar", 21)]
        string NUMERIC_BLOCK_SIZE { get; }

        [Nullable]
        [Type("varchar", 21)]
        string NUMERIC_MAX_VALUE { get; }

        [Nullable]
        [Type("varchar", 21)]
        string NUMERIC_MIN_VALUE { get; }

        [Type("varchar", 3)]
        string READ_ONLY { get; }

        [Nullable]
        [Type("varchar", 2048)]
        string SESSION_VALUE { get; }

        [Type("varchar", 2048)]
        string VARIABLE_COMMENT { get; }

        [Type("varchar", 64)]
        string VARIABLE_NAME { get; }

        [Type("varchar", 64)]
        string VARIABLE_SCOPE { get; }

        [Type("varchar", 64)]
        string VARIABLE_TYPE { get; }

    }
}