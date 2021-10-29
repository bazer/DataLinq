using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("INNODB_SYS_DATAFILES")]
    public interface INNODB_SYS_DATAFILES : IViewModel
    {
        [Type("varchar", 4000)]
        string PATH { get; }

        [Type("int")]
        int SPACE { get; }

    }
}