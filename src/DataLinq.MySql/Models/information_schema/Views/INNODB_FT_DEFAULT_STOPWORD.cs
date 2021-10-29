using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("INNODB_FT_DEFAULT_STOPWORD")]
    public interface INNODB_FT_DEFAULT_STOPWORD : IViewModel
    {
        [Type("varchar", 18)]
        string value { get; }

    }
}