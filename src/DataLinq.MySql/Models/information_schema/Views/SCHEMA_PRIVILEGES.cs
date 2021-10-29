using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("SCHEMA_PRIVILEGES")]
    public interface SCHEMA_PRIVILEGES : IViewModel
    {
        [Type("varchar", 190)]
        string GRANTEE { get; }

        [Type("varchar", 3)]
        string IS_GRANTABLE { get; }

        [Type("varchar", 64)]
        string PRIVILEGE_TYPE { get; }

        [Type("varchar", 512)]
        string TABLE_CATALOG { get; }

        [Type("varchar", 64)]
        string TABLE_SCHEMA { get; }

    }
}