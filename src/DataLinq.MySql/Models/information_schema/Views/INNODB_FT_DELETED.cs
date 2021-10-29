using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("INNODB_FT_DELETED")]
    public interface INNODB_FT_DELETED : IViewModel
    {
        [Type("bigint")]
        long DOC_ID { get; }

    }
}