using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("INNODB_SYS_INDEXES")]
    public interface INNODB_SYS_INDEXES : IViewModel
    {
        [Type("bigint")]
        long INDEX_ID { get; }

        [Type("int")]
        int MERGE_THRESHOLD { get; }

        [Type("int")]
        int N_FIELDS { get; }

        [Type("varchar", 193)]
        string NAME { get; }

        [Type("int")]
        int PAGE_NO { get; }

        [Type("int")]
        int SPACE { get; }

        [Type("bigint")]
        long TABLE_ID { get; }

        [Type("int")]
        int TYPE { get; }

    }
}