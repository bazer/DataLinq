using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
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