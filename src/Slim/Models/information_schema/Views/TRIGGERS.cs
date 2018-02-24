using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("TRIGGERS")]
    public interface TRIGGERS : IViewModel
    {
        [Nullable]
        [Type("longtext", 4294967295)]
        string ACTION_CONDITION { get; }

        [Type("bigint")]
        long ACTION_ORDER { get; }

        [Type("varchar", 9)]
        string ACTION_ORIENTATION { get; }

        [Type("varchar", 3)]
        string ACTION_REFERENCE_NEW_ROW { get; }

        [Nullable]
        [Type("varchar", 64)]
        string ACTION_REFERENCE_NEW_TABLE { get; }

        [Type("varchar", 3)]
        string ACTION_REFERENCE_OLD_ROW { get; }

        [Nullable]
        [Type("varchar", 64)]
        string ACTION_REFERENCE_OLD_TABLE { get; }

        [Type("longtext", 4294967295)]
        string ACTION_STATEMENT { get; }

        [Type("varchar", 6)]
        string ACTION_TIMING { get; }

        [Type("varchar", 32)]
        string CHARACTER_SET_CLIENT { get; }

        [Type("varchar", 32)]
        string COLLATION_CONNECTION { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? CREATED { get; }

        [Type("varchar", 32)]
        string DATABASE_COLLATION { get; }

        [Type("varchar", 189)]
        string DEFINER { get; }

        [Type("varchar", 6)]
        string EVENT_MANIPULATION { get; }

        [Type("varchar", 512)]
        string EVENT_OBJECT_CATALOG { get; }

        [Type("varchar", 64)]
        string EVENT_OBJECT_SCHEMA { get; }

        [Type("varchar", 64)]
        string EVENT_OBJECT_TABLE { get; }

        [Type("varchar", 8192)]
        string SQL_MODE { get; }

        [Type("varchar", 512)]
        string TRIGGER_CATALOG { get; }

        [Type("varchar", 64)]
        string TRIGGER_NAME { get; }

        [Type("varchar", 64)]
        string TRIGGER_SCHEMA { get; }

    }
}