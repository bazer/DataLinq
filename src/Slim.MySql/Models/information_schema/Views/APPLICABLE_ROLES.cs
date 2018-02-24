using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("APPLICABLE_ROLES")]
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