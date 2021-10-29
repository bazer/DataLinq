using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("INDEX_STATISTICS")]
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