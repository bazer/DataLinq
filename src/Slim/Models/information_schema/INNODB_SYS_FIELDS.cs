using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface INNODB_SYS_FIELDS : IViewModel
    {
        [Type("bigint")]
        long INDEX_ID { get; }

        [Type("varchar", 193)]
        string NAME { get; }

        [Type("int")]
        int POS { get; }

    }
}