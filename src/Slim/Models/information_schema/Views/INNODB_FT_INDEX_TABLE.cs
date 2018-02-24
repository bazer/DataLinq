using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("INNODB_FT_INDEX_TABLE")]
    public interface INNODB_FT_INDEX_TABLE : IViewModel
    {
        [Type("bigint")]
        long DOC_COUNT { get; }

        [Type("bigint")]
        long DOC_ID { get; }

        [Type("bigint")]
        long FIRST_DOC_ID { get; }

        [Type("bigint")]
        long LAST_DOC_ID { get; }

        [Type("bigint")]
        long POSITION { get; }

        [Type("varchar", 337)]
        string WORD { get; }

    }
}