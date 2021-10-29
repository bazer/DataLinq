using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("INNODB_FT_CONFIG")]
    public interface INNODB_FT_CONFIG : IViewModel
    {
        [Type("varchar", 193)]
        string KEY { get; }

        [Type("varchar", 193)]
        string VALUE { get; }

    }
}