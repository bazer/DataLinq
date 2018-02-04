using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface INNODB_FT_CONFIG : IViewModel
    {
        [Type("varchar", 193)]
        string KEY { get; }

        [Type("varchar", 193)]
        string VALUE { get; }

    }
}