using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("CLIENT_STATISTICS")]
    public interface CLIENT_STATISTICS : IViewModel
    {
        [Type("bigint")]
        long ACCESS_DENIED { get; }

        [Type("bigint")]
        long BINLOG_BYTES_WRITTEN { get; }

        [Type("double")]
        double BUSY_TIME { get; }

        [Type("bigint")]
        long BYTES_RECEIVED { get; }

        [Type("bigint")]
        long BYTES_SENT { get; }

        [Type("varchar", 64)]
        string CLIENT { get; }

        [Type("bigint")]
        long COMMIT_TRANSACTIONS { get; }

        [Type("bigint")]
        long CONCURRENT_CONNECTIONS { get; }

        [Type("bigint")]
        long CONNECTED_TIME { get; }

        [Type("double")]
        double CPU_TIME { get; }

        [Type("bigint")]
        long DENIED_CONNECTIONS { get; }

        [Type("bigint")]
        long EMPTY_QUERIES { get; }

        [Type("bigint")]
        long LOST_CONNECTIONS { get; }

        [Type("bigint")]
        long MAX_STATEMENT_TIME_EXCEEDED { get; }

        [Type("bigint")]
        long OTHER_COMMANDS { get; }

        [Type("bigint")]
        long ROLLBACK_TRANSACTIONS { get; }

        [Type("bigint")]
        long ROWS_DELETED { get; }

        [Type("bigint")]
        long ROWS_INSERTED { get; }

        [Type("bigint")]
        long ROWS_READ { get; }

        [Type("bigint")]
        long ROWS_SENT { get; }

        [Type("bigint")]
        long ROWS_UPDATED { get; }

        [Type("bigint")]
        long SELECT_COMMANDS { get; }

        [Type("bigint")]
        long TOTAL_CONNECTIONS { get; }

        [Type("bigint")]
        long TOTAL_SSL_CONNECTIONS { get; }

        [Type("bigint")]
        long UPDATE_COMMANDS { get; }

    }
}