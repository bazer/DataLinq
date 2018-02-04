using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface APPLICABLE_ROLES : IViewModel
    {
        [Type("varchar", 190)]
        string GRANTEE { get; }

        [Nullable]
        [Type("varchar", 3)]
        string IS_DEFAULT { get; }

        [Type("varchar", 3)]
        string IS_GRANTABLE { get; }

        [Type("varchar", 128)]
        string ROLE_NAME { get; }

    }
}