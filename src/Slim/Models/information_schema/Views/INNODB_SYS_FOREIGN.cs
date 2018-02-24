using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("INNODB_SYS_FOREIGN")]
    public interface INNODB_SYS_FOREIGN : IViewModel
    {
        [Type("varchar", 193)]
        string FOR_NAME { get; }

        [Type("varchar", 193)]
        string ID { get; }

        [Type("int")]
        int N_COLS { get; }

        [Type("varchar", 193)]
        string REF_NAME { get; }

        [Type("int")]
        int TYPE { get; }

    }
}