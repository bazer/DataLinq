using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("TABLESPACES")]
    public interface TABLESPACES : IViewModel
    {
        [Nullable]
        [Type("bigint")]
        long? AUTOEXTEND_SIZE { get; }

        [Type("varchar", 64)]
        string ENGINE { get; }

        [Nullable]
        [Type("bigint")]
        long? EXTENT_SIZE { get; }

        [Nullable]
        [Type("varchar", 64)]
        string LOGFILE_GROUP_NAME { get; }

        [Nullable]
        [Type("bigint")]
        long? MAXIMUM_SIZE { get; }

        [Nullable]
        [Type("bigint")]
        long? NODEGROUP_ID { get; }

        [Nullable]
        [Type("varchar", 2048)]
        string TABLESPACE_COMMENT { get; }

        [Type("varchar", 64)]
        string TABLESPACE_NAME { get; }

        [Nullable]
        [Type("varchar", 64)]
        string TABLESPACE_TYPE { get; }

    }
}