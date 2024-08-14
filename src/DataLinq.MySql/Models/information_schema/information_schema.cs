using System;
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace DataLinq.MySql.Models;

[Database("information_schema")]
public partial class information_schema(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<COLUMNS> COLUMNS { get; } = new DbRead<COLUMNS>(dataSource);
    public DbRead<KEY_COLUMN_USAGE> KEY_COLUMN_USAGE { get; } = new DbRead<KEY_COLUMN_USAGE>(dataSource);
    public DbRead<STATISTICS> STATISTICS { get; } = new DbRead<STATISTICS>(dataSource);
    public DbRead<TABLES> TABLES { get; } = new DbRead<TABLES>(dataSource);
    public DbRead<VIEWS> VIEWS { get; } = new DbRead<VIEWS>(dataSource);
}