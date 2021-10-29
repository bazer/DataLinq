using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("INNODB_SYS_TABLES")]
    public interface INNODB_SYS_TABLES : IViewModel
    {
        [Nullable]
        [Type("varchar", 10)]
        string FILE_FORMAT { get; }

        [Type("int")]
        int FLAG { get; }

        [Type("int")]
        int N_COLS { get; }

        [Type("varchar", 655)]
        string NAME { get; }

        [Nullable]
        [Type("varchar", 12)]
        string ROW_FORMAT { get; }

        [Type("int")]
        int SPACE { get; }

        [Nullable]
        [Type("varchar", 10)]
        string SPACE_TYPE { get; }

        [Type("bigint")]
        long TABLE_ID { get; }

        [Type("int")]
        int ZIP_PAGE_SIZE { get; }

    }
}