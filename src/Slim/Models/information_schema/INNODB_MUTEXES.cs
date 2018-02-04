using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface INNODB_MUTEXES : IViewModel
    {
        [Type("varchar", 4000)]
        string CREATE_FILE { get; }

        [Type("int")]
        int CREATE_LINE { get; }

        [Type("varchar", 4000)]
        string NAME { get; }

        [Type("bigint")]
        long OS_WAITS { get; }

    }
}