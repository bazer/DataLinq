using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface INNODB_FT_DELETED : IViewModel
    {
        [Type("bigint")]
        long DOC_ID { get; }

    }
}