using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("INNODB_MUTEXES")]
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