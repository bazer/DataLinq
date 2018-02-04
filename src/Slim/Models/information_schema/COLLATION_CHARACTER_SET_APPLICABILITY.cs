using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface COLLATION_CHARACTER_SET_APPLICABILITY : IViewModel
    {
        [Type("varchar", 32)]
        string CHARACTER_SET_NAME { get; }

        [Type("varchar", 32)]
        string COLLATION_NAME { get; }

    }
}