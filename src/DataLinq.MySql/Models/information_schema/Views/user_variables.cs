using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("user_variables")]
    public interface user_variables : IViewModel
    {
        [Nullable]
        [Type("varchar", 32)]
        string CHARACTER_SET_NAME { get; }

        [Type("varchar", 64)]
        string VARIABLE_NAME { get; }

        [Type("varchar", 64)]
        string VARIABLE_TYPE { get; }

        [Nullable]
        [Type("varchar", 2048)]
        string VARIABLE_VALUE { get; }

    }
}