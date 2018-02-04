using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface VIEWS : IViewModel
    {
        [Type("varchar", 10)]
        string ALGORITHM { get; }

        [Type("varchar", 32)]
        string CHARACTER_SET_CLIENT { get; }

        [Type("varchar", 8)]
        string CHECK_OPTION { get; }

        [Type("varchar", 32)]
        string COLLATION_CONNECTION { get; }

        [Type("varchar", 189)]
        string DEFINER { get; }

        [Type("varchar", 3)]
        string IS_UPDATABLE { get; }

        [Type("varchar", 7)]
        string SECURITY_TYPE { get; }

        [Type("varchar", 512)]
        string TABLE_CATALOG { get; }

        [Type("varchar", 64)]
        string TABLE_NAME { get; }

        [Type("varchar", 64)]
        string TABLE_SCHEMA { get; }

        [Type("longtext", 4294967295)]
        string VIEW_DEFINITION { get; }

    }
}