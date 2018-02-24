using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("EVENTS")]
    public interface EVENTS : IViewModel
    {
        [Type("varchar", 32)]
        string CHARACTER_SET_CLIENT { get; }

        [Type("varchar", 32)]
        string COLLATION_CONNECTION { get; }

        [Type("datetime")]
        DateTime CREATED { get; }

        [Type("varchar", 32)]
        string DATABASE_COLLATION { get; }

        [Type("varchar", 189)]
        string DEFINER { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? ENDS { get; }

        [Type("varchar", 8)]
        string EVENT_BODY { get; }

        [Type("varchar", 64)]
        string EVENT_CATALOG { get; }

        [Type("varchar", 64)]
        string EVENT_COMMENT { get; }

        [Type("longtext", 4294967295)]
        string EVENT_DEFINITION { get; }

        [Type("varchar", 64)]
        string EVENT_NAME { get; }

        [Type("varchar", 64)]
        string EVENT_SCHEMA { get; }

        [Type("varchar", 9)]
        string EVENT_TYPE { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? EXECUTE_AT { get; }

        [Nullable]
        [Type("varchar", 18)]
        string INTERVAL_FIELD { get; }

        [Nullable]
        [Type("varchar", 256)]
        string INTERVAL_VALUE { get; }

        [Type("datetime")]
        DateTime LAST_ALTERED { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? LAST_EXECUTED { get; }

        [Type("varchar", 12)]
        string ON_COMPLETION { get; }

        [Type("bigint")]
        long ORIGINATOR { get; }

        [Type("varchar", 8192)]
        string SQL_MODE { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? STARTS { get; }

        [Type("varchar", 18)]
        string STATUS { get; }

        [Type("varchar", 64)]
        string TIME_ZONE { get; }

    }
}