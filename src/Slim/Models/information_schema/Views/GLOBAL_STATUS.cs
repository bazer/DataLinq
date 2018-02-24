using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("GLOBAL_STATUS")]
    public interface GLOBAL_STATUS : IViewModel
    {
        [Type("varchar", 64)]
        string VARIABLE_NAME { get; }

        [Type("varchar", 2048)]
        string VARIABLE_VALUE { get; }

    }
}