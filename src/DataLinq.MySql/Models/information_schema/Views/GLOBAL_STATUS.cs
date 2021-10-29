using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("GLOBAL_STATUS")]
    public interface GLOBAL_STATUS : IViewModel
    {
        [Type("varchar", 64)]
        string VARIABLE_NAME { get; }

        [Type("varchar", 2048)]
        string VARIABLE_VALUE { get; }

    }
}