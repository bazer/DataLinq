using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("INNODB_SYS_DATAFILES")]
    public interface INNODB_SYS_DATAFILES : IViewModel
    {
        [Type("varchar", 4000)]
        string PATH { get; }

        [Type("int")]
        int SPACE { get; }

    }
}