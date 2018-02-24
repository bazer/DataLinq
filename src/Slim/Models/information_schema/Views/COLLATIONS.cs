using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("COLLATIONS")]
    public interface COLLATIONS : IViewModel
    {
        [Type("varchar", 32)]
        string CHARACTER_SET_NAME { get; }

        [Type("varchar", 32)]
        string COLLATION_NAME { get; }

        [Type("bigint")]
        long ID { get; }

        [Type("varchar", 3)]
        string IS_COMPILED { get; }

        [Type("varchar", 3)]
        string IS_DEFAULT { get; }

        [Type("bigint")]
        long SORTLEN { get; }

    }
}