using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("USER_STATISTICS")]
    public interface USER_STATISTICS : IViewModel
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

        [Type("bigint")]
        long COMMIT_TRANSACTIONS { get; }

        [Type("int")]
        int CONCURRENT_CONNECTIONS { get; }

        [Type("int")]
        int CONNECTED_TIME { get; }

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

        [Type("int")]
        int TOTAL_CONNECTIONS { get; }

        [Type("bigint")]
        long TOTAL_SSL_CONNECTIONS { get; }

        [Type("bigint")]
        long UPDATE_COMMANDS { get; }

        [Type("varchar", 128)]
        string USER { get; }

    }
}