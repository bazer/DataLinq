using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("SPATIAL_REF_SYS")]
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