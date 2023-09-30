using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;

namespace DataLinq.MySql.Models
{
    [Database("information_schema")]
    public interface information_schema : IDatabaseModel
    {
        DbRead<COLUMNS> COLUMNS { get; }
        DbRead<KEY_COLUMN_USAGE> KEY_COLUMN_USAGE { get; }
        DbRead<TABLES> TABLES { get; }
        DbRead<VIEWS> VIEWS { get; }
    }
}