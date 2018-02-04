using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface INNODB_SYS_VIRTUAL : IViewModel
    {
        [Type("int")]
        int BASE_POS { get; }

        [Type("int")]
        int POS { get; }

        [Type("bigint")]
        long TABLE_ID { get; }

    }
}