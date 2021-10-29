using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("ENABLED_ROLES")]
    public interface ENABLED_ROLES : IViewModel
    {
        [Nullable]
        [Type("varchar", 128)]
        string ROLE_NAME { get; }

    }
}