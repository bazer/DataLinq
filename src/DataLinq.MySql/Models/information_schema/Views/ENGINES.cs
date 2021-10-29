using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Name("ENGINES")]
    public interface ENGINES : IViewModel
    {
        [Type("varchar", 160)]
        string COMMENT { get; }

        [Type("varchar", 64)]
        string ENGINE { get; }

        [Nullable]
        [Type("varchar", 3)]
        string SAVEPOINTS { get; }

        [Type("varchar", 8)]
        string SUPPORT { get; }

        [Nullable]
        [Type("varchar", 3)]
        string TRANSACTIONS { get; }

        [Nullable]
        [Type("varchar", 3)]
        string XA { get; }

    }
}