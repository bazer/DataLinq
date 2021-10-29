using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("SCHEMATA")]
    public interface SCHEMATA : IViewModel
    {
        [Type("varchar", 512)]
        string CATALOG_NAME { get; }

        [Type("varchar", 32)]
        string DEFAULT_CHARACTER_SET_NAME { get; }

        [Type("varchar", 32)]
        string DEFAULT_COLLATION_NAME { get; }

        [Type("varchar", 64)]
        string SCHEMA_NAME { get; }

        [Nullable]
        [Type("varchar", 512)]
        string SQL_PATH { get; }

    }
}