using DataLinq.Attributes;
using DataLinq.Interfaces;

namespace DataLinq.MySql.Models;

[Database("information_schema")]
public partial class information_schema : IDatabaseModel
{
    public virtual DbRead<COLUMNS> COLUMNS { get; }
    public virtual DbRead<KEY_COLUMN_USAGE> KEY_COLUMN_USAGE { get; }
    public virtual DbRead<STATISTICS> STATISTICS { get; }
    public virtual DbRead<TABLES> TABLES { get; }
    public virtual DbRead<VIEWS> VIEWS { get; }
}