using System;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.MySql.Shared;

namespace DataLinq.MySql.information_schema;

[Database("mysql_information_schema")]
public partial class MySQLInformationSchema(DataSourceAccess dataSource) : IDatabaseModel, IInformationSchema
{
    public DbRead<COLUMNS> COLUMNS { get; } = new DbRead<COLUMNS>(dataSource);
    public DbRead<KEY_COLUMN_USAGE> KEY_COLUMN_USAGE { get; } = new DbRead<KEY_COLUMN_USAGE>(dataSource);
    public DbRead<STATISTICS> STATISTICS { get; } = new DbRead<STATISTICS>(dataSource);
    public DbRead<TABLES> TABLES { get; } = new DbRead<TABLES>(dataSource);
    public DbRead<VIEWS> VIEWS { get; } = new DbRead<VIEWS>(dataSource);
}