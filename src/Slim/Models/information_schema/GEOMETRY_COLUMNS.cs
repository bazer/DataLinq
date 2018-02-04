using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface GEOMETRY_COLUMNS : IViewModel
    {
        [Type("tinyint")]
        int COORD_DIMENSION { get; }

        [Type("varchar", 64)]
        string F_GEOMETRY_COLUMN { get; }

        [Type("varchar", 512)]
        string F_TABLE_CATALOG { get; }

        [Type("varchar", 64)]
        string F_TABLE_NAME { get; }

        [Type("varchar", 64)]
        string F_TABLE_SCHEMA { get; }

        [Type("varchar", 64)]
        string G_GEOMETRY_COLUMN { get; }

        [Type("varchar", 512)]
        string G_TABLE_CATALOG { get; }

        [Type("varchar", 64)]
        string G_TABLE_NAME { get; }

        [Type("varchar", 64)]
        string G_TABLE_SCHEMA { get; }

        [Type("int")]
        int GEOMETRY_TYPE { get; }

        [Type("tinyint")]
        int MAX_PPR { get; }

        [Type("smallint")]
        int SRID { get; }

        [Type("tinyint")]
        int STORAGE_TYPE { get; }

    }
}