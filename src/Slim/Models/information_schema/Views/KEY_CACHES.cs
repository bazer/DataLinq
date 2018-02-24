using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("KEY_CACHES")]
    public interface KEY_CACHES : IViewModel
    {
        [Type("bigint")]
        long BLOCK_SIZE { get; }

        [Type("bigint")]
        long DIRTY_BLOCKS { get; }

        [Type("bigint")]
        long FULL_SIZE { get; }

        [Type("varchar", 192)]
        string KEY_CACHE_NAME { get; }

        [Type("bigint")]
        long READ_REQUESTS { get; }

        [Type("bigint")]
        long READS { get; }

        [Nullable]
        [Type("int")]
        int? SEGMENT_NUMBER { get; }

        [Nullable]
        [Type("int")]
        int? SEGMENTS { get; }

        [Type("bigint")]
        long UNUSED_BLOCKS { get; }

        [Type("bigint")]
        long USED_BLOCKS { get; }

        [Type("bigint")]
        long WRITE_REQUESTS { get; }

        [Type("bigint")]
        long WRITES { get; }

    }
}