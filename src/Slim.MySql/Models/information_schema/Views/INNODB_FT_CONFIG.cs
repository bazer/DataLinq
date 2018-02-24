using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("INNODB_FT_CONFIG")]
    public interface INNODB_FT_CONFIG : IViewModel
    {
        [Type("varchar", 193)]
        string KEY { get; }

        [Type("varchar", 193)]
        string VALUE { get; }

    }
}