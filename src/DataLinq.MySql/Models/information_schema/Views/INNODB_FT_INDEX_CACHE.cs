using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("INNODB_FT_INDEX_CACHE")]
    public interface INNODB_FT_INDEX_CACHE : IViewModel
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