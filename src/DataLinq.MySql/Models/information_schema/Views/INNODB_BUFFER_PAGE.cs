using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("INNODB_BUFFER_PAGE")]
    public interface INNODB_BUFFER_PAGE : IViewModel
    {
        [Type("bigint")]
        long ACCESS_TIME { get; }

        [Type("bigint")]
        long BLOCK_ID { get; }

        [Type("bigint")]
        long COMPRESSED_SIZE { get; }

        [Type("bigint")]
        long DATA_SIZE { get; }

        [Type("bigint")]
        long FIX_COUNT { get; }

        [Type("bigint")]
        long FLUSH_TYPE { get; }

        [Type("bigint")]
        long FREE_PAGE_CLOCK { get; }

        [Nullable]
        [Type("varchar", 1024)]
        string INDEX_NAME { get; }

        [Nullable]
        [Type("varchar", 64)]
        string IO_FIX { get; }

        [Nullable]
        [Type("varchar", 3)]
        string IS_HASHED { get; }

        [Nullable]
        [Type("varchar", 3)]
        string IS_OLD { get; }

        [Type("bigint")]
        long NEWEST_MODIFICATION { get; }

        [Type("bigint")]
        long NUMBER_RECORDS { get; }

        [Type("bigint")]
        long OLDEST_MODIFICATION { get; }

        [Type("bigint")]
        long PAGE_NUMBER { get; }

        [Nullable]
        [Type("varchar", 64)]
        string PAGE_STATE { get; }

        [Nullable]
        [Type("varchar", 64)]
        string PAGE_TYPE { get; }

        [Type("bigint")]
        long POOL_ID { get; }

        [Type("bigint")]
        long SPACE { get; }

        [Nullable]
        [Type("varchar", 1024)]
        string TABLE_NAME { get; }

    }
}