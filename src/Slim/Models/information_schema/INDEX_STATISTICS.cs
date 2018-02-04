using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    public interface INDEX_STATISTICS : IViewModel
    {
        [Type("varchar", 192)]
        string INDEX_NAME { get; }

        [Type("bigint")]
        long ROWS_READ { get; }

        [Type("varchar", 192)]
        string TABLE_NAME { get; }

        [Type("varchar", 192)]
        string TABLE_SCHEMA { get; }

    }
}