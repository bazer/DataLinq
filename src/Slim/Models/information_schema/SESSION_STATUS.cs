using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface SESSION_STATUS : IViewModel
    {
        [Type("varchar", 64)]
        string VARIABLE_NAME { get; }

        [Type("varchar", 2048)]
        string VARIABLE_VALUE { get; }

    }
}