using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("CHARACTER_SETS")]
    public interface CHARACTER_SETS : IViewModel
    {
        [Type("varchar", 32)]
        string CHARACTER_SET_NAME { get; }

        [Type("varchar", 32)]
        string DEFAULT_COLLATE_NAME { get; }

        [Type("varchar", 60)]
        string DESCRIPTION { get; }

        [Type("bigint")]
        long MAXLEN { get; }

    }
}