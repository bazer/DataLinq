using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface INNODB_SYS_TABLESPACES : IViewModel
    {
        [Type("bigint")]
        long ALLOCATED_SIZE { get; }

        [Nullable]
        [Type("varchar", 10)]
        string FILE_FORMAT { get; }

        [Type("bigint")]
        long FILE_SIZE { get; }

        [Type("int")]
        int FLAG { get; }

        [Type("int")]
        int FS_BLOCK_SIZE { get; }

        [Type("varchar", 655)]
        string NAME { get; }

        [Type("int")]
        int PAGE_SIZE { get; }

        [Nullable]
        [Type("varchar", 22)]
        string ROW_FORMAT { get; }

        [Type("int")]
        int SPACE { get; }

        [Nullable]
        [Type("varchar", 10)]
        string SPACE_TYPE { get; }

        [Type("int")]
        int ZIP_PAGE_SIZE { get; }

    }
}