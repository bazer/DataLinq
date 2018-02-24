using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("INNODB_TABLESPACES_ENCRYPTION")]
    public interface INNODB_TABLESPACES_ENCRYPTION : IViewModel
    {
        [Type("int")]
        int CURRENT_KEY_ID { get; }

        [Type("int")]
        int CURRENT_KEY_VERSION { get; }

        [Type("int")]
        int ENCRYPTION_SCHEME { get; }

        [Nullable]
        [Type("bigint")]
        long? KEY_ROTATION_MAX_PAGE_NUMBER { get; }

        [Nullable]
        [Type("bigint")]
        long? KEY_ROTATION_PAGE_NUMBER { get; }

        [Type("int")]
        int KEYSERVER_REQUESTS { get; }

        [Type("int")]
        int MIN_KEY_VERSION { get; }

        [Nullable]
        [Type("varchar", 655)]
        string NAME { get; }

        [Type("int")]
        int ROTATING_OR_FLUSHING { get; }

        [Type("int")]
        int SPACE { get; }

    }
}