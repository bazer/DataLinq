using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("INNODB_SYS_FIELDS")]
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