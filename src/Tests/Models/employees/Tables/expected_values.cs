using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    [Name("expected_values")]
    public interface expected_values : ITableModel
    {
        [PrimaryKey]
        [Type("varchar", 30)]
        string table_name { get; }

        [Type("varchar", 100)]
        string crc_md5 { get; }

        [Type("varchar", 100)]
        string crc_sha { get; }

        [Type("int")]
        int recs { get; }

    }
}