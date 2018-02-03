using System;
using Slim.Interfaces;
using Slim.Attributes;

namespace Tests.Models
{
    public interface expected_values : ITableModel
    {
        string crc_md5 { get; }

        string crc_sha { get; }

        int recs { get; }

        [PrimaryKey]
        string table_name { get; }

    }
}