using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface INNODB_SYS_DATAFILES : IViewModel
    {
        [Type("varchar", 4000)]
        string PATH { get; }

        [Type("int")]
        int SPACE { get; }

    }
}