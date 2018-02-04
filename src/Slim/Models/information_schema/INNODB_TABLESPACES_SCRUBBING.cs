using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface INNODB_TABLESPACES_SCRUBBING : IViewModel
    {
        [Type("int")]
        int COMPRESSED { get; }

        [Nullable]
        [Type("int")]
        int? CURRENT_SCRUB_ACTIVE_THREADS { get; }

        [Type("bigint")]
        long CURRENT_SCRUB_MAX_PAGE_NUMBER { get; }

        [Type("bigint")]
        long CURRENT_SCRUB_PAGE_NUMBER { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? CURRENT_SCRUB_STARTED { get; }

        [Nullable]
        [Type("datetime")]
        DateTime? LAST_SCRUB_COMPLETED { get; }

        [Nullable]
        [Type("varchar", 655)]
        string NAME { get; }

        [Type("int")]
        int ROTATING_OR_FLUSHING { get; }

        [Type("bigint")]
        long SPACE { get; }

    }
}