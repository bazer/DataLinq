using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("INNODB_BUFFER_POOL_STATS")]
    public interface INNODB_BUFFER_POOL_STATS : IViewModel
    {
        [Type("bigint")]
        long DATABASE_PAGES { get; }

        [Type("bigint")]
        long FREE_BUFFERS { get; }

        [Type("bigint")]
        long HIT_RATE { get; }

        [Type("bigint")]
        long LRU_IO_CURRENT { get; }

        [Type("bigint")]
        long LRU_IO_TOTAL { get; }

        [Type("bigint")]
        long MODIFIED_DATABASE_PAGES { get; }

        [Type("bigint")]
        long NOT_YOUNG_MAKE_PER_THOUSAND_GETS { get; }

        [Type("bigint")]
        long NUMBER_PAGES_CREATED { get; }

        [Type("bigint")]
        long NUMBER_PAGES_GET { get; }

        [Type("bigint")]
        long NUMBER_PAGES_READ { get; }

        [Type("bigint")]
        long NUMBER_PAGES_READ_AHEAD { get; }

        [Type("bigint")]
        long NUMBER_PAGES_WRITTEN { get; }

        [Type("bigint")]
        long NUMBER_READ_AHEAD_EVICTED { get; }

        [Type("bigint")]
        long OLD_DATABASE_PAGES { get; }

        [Type("double")]
        double PAGES_CREATE_RATE { get; }

        [Type("double")]
        double PAGES_MADE_NOT_YOUNG_RATE { get; }

        [Type("bigint")]
        long PAGES_MADE_YOUNG { get; }

        [Type("double")]
        double PAGES_MADE_YOUNG_RATE { get; }

        [Type("bigint")]
        long PAGES_NOT_MADE_YOUNG { get; }

        [Type("double")]
        double PAGES_READ_RATE { get; }

        [Type("double")]
        double PAGES_WRITTEN_RATE { get; }

        [Type("bigint")]
        long PENDING_DECOMPRESS { get; }

        [Type("bigint")]
        long PENDING_FLUSH_LIST { get; }

        [Type("bigint")]
        long PENDING_FLUSH_LRU { get; }

        [Type("bigint")]
        long PENDING_READS { get; }

        [Type("bigint")]
        long POOL_ID { get; }

        [Type("bigint")]
        long POOL_SIZE { get; }

        [Type("double")]
        double READ_AHEAD_EVICTED_RATE { get; }

        [Type("double")]
        double READ_AHEAD_RATE { get; }

        [Type("bigint")]
        long UNCOMPRESS_CURRENT { get; }

        [Type("bigint")]
        long UNCOMPRESS_TOTAL { get; }

        [Type("bigint")]
        long YOUNG_MAKE_PER_THOUSAND_GETS { get; }

    }
}