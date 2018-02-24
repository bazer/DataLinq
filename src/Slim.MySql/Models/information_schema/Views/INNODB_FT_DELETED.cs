using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("INNODB_FT_DELETED")]
    public interface INNODB_FT_DELETED : IViewModel
    {
        [Type("bigint")]
        long DOC_ID { get; }

    }
}