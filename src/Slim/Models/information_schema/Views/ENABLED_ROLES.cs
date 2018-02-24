using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("ENABLED_ROLES")]
    public interface ENABLED_ROLES : IViewModel
    {
        [Nullable]
        [Type("varchar", 128)]
        string ROLE_NAME { get; }

    }
}