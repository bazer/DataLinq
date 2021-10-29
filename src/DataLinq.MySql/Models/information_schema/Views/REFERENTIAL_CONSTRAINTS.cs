using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("REFERENTIAL_CONSTRAINTS")]
    public interface REFERENTIAL_CONSTRAINTS : IViewModel
    {
        [Type("varchar", 512)]
        string CONSTRAINT_CATALOG { get; }

        [Type("varchar", 64)]
        string CONSTRAINT_NAME { get; }

        [Type("varchar", 64)]
        string CONSTRAINT_SCHEMA { get; }

        [Type("varchar", 64)]
        string DELETE_RULE { get; }

        [Type("varchar", 64)]
        string MATCH_OPTION { get; }

        [Type("varchar", 64)]
        string REFERENCED_TABLE_NAME { get; }

        [Type("varchar", 64)]
        string TABLE_NAME { get; }

        [Type("varchar", 512)]
        string UNIQUE_CONSTRAINT_CATALOG { get; }

        [Nullable]
        [Type("varchar", 64)]
        string UNIQUE_CONSTRAINT_NAME { get; }

        [Type("varchar", 64)]
        string UNIQUE_CONSTRAINT_SCHEMA { get; }

        [Type("varchar", 64)]
        string UPDATE_RULE { get; }

    }
}