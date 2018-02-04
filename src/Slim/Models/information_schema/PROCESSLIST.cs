using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface PROCESSLIST : IViewModel
    {
        [Type("varchar", 16)]
        string COMMAND { get; }

        [Nullable]
        [Type("varchar", 64)]
        string DB { get; }

        [Type("int")]
        int EXAMINED_ROWS { get; }

        [Type("varchar", 64)]
        string HOST { get; }

        [Type("bigint")]
        long ID { get; }

        [Nullable]
        [Type("longtext", 4294967295)]
        string INFO { get; }

        [Nullable]
        [Type("blob", 65535)]
        byte[] INFO_BINARY { get; }

        [Type("tinyint")]
        int MAX_STAGE { get; }

        [Type("bigint")]
        long MEMORY_USED { get; }

        [Type("decimal")]
        decimal PROGRESS { get; }

        [Type("bigint")]
        long QUERY_ID { get; }

        [Type("tinyint")]
        int STAGE { get; }

        [Nullable]
        [Type("varchar", 64)]
        string STATE { get; }

        [Type("bigint")]
        long TID { get; }

        [Type("int")]
        int TIME { get; }

        [Type("decimal")]
        decimal TIME_MS { get; }

        [Type("varchar", 128)]
        string USER { get; }

    }
}