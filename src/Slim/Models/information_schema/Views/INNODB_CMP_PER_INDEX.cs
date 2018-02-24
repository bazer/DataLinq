using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.Models
{
    [Name("INNODB_CMP_PER_INDEX")]
    public interface INNODB_CMP_PER_INDEX : IViewModel
    {
        [Type("int")]
        int compress_ops { get; }

        [Type("int")]
        int compress_ops_ok { get; }

        [Type("int")]
        int compress_time { get; }

        [Type("varchar", 192)]
        string database_name { get; }

        [Type("varchar", 192)]
        string index_name { get; }

        [Type("varchar", 192)]
        string table_name { get; }

        [Type("int")]
        int uncompress_ops { get; }

        [Type("int")]
        int uncompress_time { get; }

    }
}