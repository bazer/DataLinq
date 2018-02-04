using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface INNODB_SYS_COLUMNS : IViewModel
    {
        [Type("int")]
        int LEN { get; }

        [Type("int")]
        int MTYPE { get; }

        [Type("varchar", 193)]
        string NAME { get; }

        [Type("bigint")]
        long POS { get; }

        [Type("int")]
        int PRTYPE { get; }

        [Type("bigint")]
        long TABLE_ID { get; }

    }
}