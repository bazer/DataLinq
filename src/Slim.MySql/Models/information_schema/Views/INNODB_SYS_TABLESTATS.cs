using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("INNODB_SYS_TABLESTATS")]
    public interface INNODB_SYS_TABLESTATS : IViewModel
    {
        [Type("bigint")]
        long AUTOINC { get; }

        [Type("bigint")]
        long CLUST_INDEX_SIZE { get; }

        [Type("bigint")]
        long MODIFIED_COUNTER { get; }

        [Type("varchar", 193)]
        string NAME { get; }

        [Type("bigint")]
        long NUM_ROWS { get; }

        [Type("bigint")]
        long OTHER_INDEX_SIZE { get; }

        [Type("int")]
        int REF_COUNT { get; }

        [Type("varchar", 193)]
        string STATS_INITIALIZED { get; }

        [Type("bigint")]
        long TABLE_ID { get; }

    }
}