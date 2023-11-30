using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.MySql.Models;

[Database("information_schema")]
public interface information_schema : IDatabaseModel
{
    DbRead<COLUMNS> COLUMNS { get; }
    DbRead<KEY_COLUMN_USAGE> KEY_COLUMN_USAGE { get; }
    DbRead<STATISTICS> STATISTICS { get; }
    DbRead<TABLES> TABLES { get; }
    DbRead<VIEWS> VIEWS { get; }
}