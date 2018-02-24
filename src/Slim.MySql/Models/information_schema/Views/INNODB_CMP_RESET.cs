using System;
using Slim;
using Slim.Interfaces;
using Slim.Attributes;

namespace Slim.MySql.Models
{
    [Name("INNODB_CMP_RESET")]
    public interface INNODB_CMP_RESET : IViewModel
    {
        [Type("int")]
        int compress_ops { get; }

        [Type("int")]
        int compress_ops_ok { get; }

        [Type("int")]
        int compress_time { get; }

        [Type("int")]
        int page_size { get; }

        [Type("int")]
        int uncompress_ops { get; }

        [Type("int")]
        int uncompress_time { get; }

    }
}