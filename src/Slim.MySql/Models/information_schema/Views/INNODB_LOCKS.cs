using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("INNODB_LOCKS")]
    public interface INNODB_LOCKS : IViewModel
    {
        [Nullable]
        [Type("varchar", 8192)]
        string lock_data { get; }

        [Type("varchar", 81)]
        string lock_id { get; }

        [Nullable]
        [Type("varchar", 1024)]
        string lock_index { get; }

        [Type("varchar", 32)]
        string lock_mode { get; }

        [Nullable]
        [Type("bigint")]
        long? lock_page { get; }

        [Nullable]
        [Type("bigint")]
        long? lock_rec { get; }

        [Nullable]
        [Type("bigint")]
        long? lock_space { get; }

        [Type("varchar", 1024)]
        string lock_table { get; }

        [Type("varchar", 18)]
        string lock_trx_id { get; }

        [Type("varchar", 32)]
        string lock_type { get; }

    }
}