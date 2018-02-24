using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("INNODB_METRICS")]
    public interface INNODB_METRICS : IViewModel
    {
        [Nullable]
        [Type("double")]
        double? AVG_COUNT { get; }

        [Nullable]
        [Type("double")]
        double? AVG_COUNT_RESET { get; }

        [Type("varchar", 193)]
        string COMMENT { get; }

        [Type("bigint")]
        long COUNT { get; }

        [Type("bigint")]
        long COUNT_RESET { get; }

        [Nullable]
        [Type("bigint")]
        long? MAX_COUNT { get; }

        [Nullable]
        [Type("bigint")]
        long? MAX_COUNT_RESET { get; }

        [Nullable]
        [Type("bigint")]
        long? MIN_COUNT { get; }

        [Nullable]
        [Type("bigint")]
        long? MIN_COUNT_RESET { get; }

        [Type("varchar", 193)]
        string NAME { get; }

        [Type("varchar", 193)]
        string STATUS { get; }

        [Type("varchar", 193)]
        string SUBSYSTEM { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? TIME_DISABLED { get; }

        [Nullable]
        [Type("bigint")]
        long? TIME_ELAPSED { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? TIME_ENABLED { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? TIME_RESET { get; }

        [Type("varchar", 193)]
        string TYPE { get; }

    }
}