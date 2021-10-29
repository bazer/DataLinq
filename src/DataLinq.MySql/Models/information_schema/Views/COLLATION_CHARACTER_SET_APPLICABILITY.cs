using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("COLLATION_CHARACTER_SET_APPLICABILITY")]
    public interface COLLATION_CHARACTER_SET_APPLICABILITY : IViewModel
    {
        [Type("varchar", 32)]
        string CHARACTER_SET_NAME { get; }

        [Type("varchar", 32)]
        string COLLATION_NAME { get; }

    }
}