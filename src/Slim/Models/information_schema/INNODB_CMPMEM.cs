using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface INNODB_CMPMEM : IViewModel
    {
        [Type("int")]
        int buffer_pool_instance { get; }

        [Type("int")]
        int page_size { get; }

        [Type("int")]
        int pages_free { get; }

        [Type("int")]
        int pages_used { get; }

        [Type("bigint")]
        long relocation_ops { get; }

        [Type("int")]
        int relocation_time { get; }

    }
}