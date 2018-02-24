using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("COLLATION_CHARACTER_SET_APPLICABILITY")]
    public interface COLLATION_CHARACTER_SET_APPLICABILITY : IViewModel
    {
        [Type("varchar", 32)]
        string CHARACTER_SET_NAME { get; }

        [Type("varchar", 32)]
        string COLLATION_NAME { get; }

    }
}