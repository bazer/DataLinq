using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface SPATIAL_REF_SYS : IViewModel
    {
        [Type("varchar", 512)]
        string AUTH_NAME { get; }

        [Type("int")]
        int AUTH_SRID { get; }

        [Type("smallint")]
        int SRID { get; }

        [Type("varchar", 2048)]
        string SRTEXT { get; }

    }
}