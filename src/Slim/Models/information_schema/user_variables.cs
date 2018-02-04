using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface user_variables : IViewModel
    {
        [Nullable]
        [Type("varchar", 32)]
        string CHARACTER_SET_NAME { get; }

        [Type("varchar", 64)]
        string VARIABLE_NAME { get; }

        [Type("varchar", 64)]
        string VARIABLE_TYPE { get; }

        [Nullable]
        [Type("varchar", 2048)]
        string VARIABLE_VALUE { get; }

    }
}