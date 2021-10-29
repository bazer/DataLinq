using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("INNODB_SYS_SEMAPHORE_WAITS")]
    public interface INNODB_SYS_SEMAPHORE_WAITS : IViewModel
    {
        [Nullable]
        [Type("varchar", 4000)]
        string CREATED_FILE { get; }

        [Type("int")]
        int CREATED_LINE { get; }

        [Nullable]
        [Type("varchar", 4000)]
        string FILE { get; }

        [Nullable]
        [Type("varchar", 4000)]
        string HOLDER_FILE { get; }

        [Type("int")]
        int HOLDER_LINE { get; }

        [Type("bigint")]
        long HOLDER_THREAD_ID { get; }

        [Nullable]
        [Type("varchar", 4000)]
        string LAST_READER_FILE { get; }

        [Type("int")]
        int LAST_READER_LINE { get; }

        [Nullable]
        [Type("varchar", 4000)]
        string LAST_WRITER_FILE { get; }

        [Type("int")]
        int LAST_WRITER_LINE { get; }

        [Type("int")]
        int LINE { get; }

        [Type("bigint")]
        long LOCK_WORD { get; }

        [Nullable]
        [Type("varchar", 4000)]
        string OBJECT_NAME { get; }

        [Type("int")]
        int OS_WAIT_COUNT { get; }

        [Type("int")]
        int READERS { get; }

        [Nullable]
        [Type("varchar", 16)]
        string RESERVATION_MODE { get; }

        [Type("bigint")]
        long THREAD_ID { get; }

        [Type("bigint")]
        long WAIT_OBJECT { get; }

        [Type("bigint")]
        long WAIT_TIME { get; }

        [Nullable]
        [Type("varchar", 16)]
        string WAIT_TYPE { get; }

        [Type("bigint")]
        long WAITERS_FLAG { get; }

        [Type("bigint")]
        long WRITER_THREAD { get; }

    }
}