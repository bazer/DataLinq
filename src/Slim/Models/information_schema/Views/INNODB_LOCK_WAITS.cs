using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("INNODB_LOCK_WAITS")]
    public interface INNODB_LOCK_WAITS : IViewModel
    {
        [Type("varchar", 81)]
        string blocking_lock_id { get; }

        [Type("varchar", 18)]
        string blocking_trx_id { get; }

        [Type("varchar", 81)]
        string requested_lock_id { get; }

        [Type("varchar", 18)]
        string requesting_trx_id { get; }

    }
}