using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface ENABLED_ROLES : IViewModel
    {
        [Nullable]
        [Type("varchar", 128)]
        string ROLE_NAME { get; }

    }
}