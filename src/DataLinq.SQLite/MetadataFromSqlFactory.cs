using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite
{
    public static class MetadataFromSqlFactory
    {
        public static DatabaseMetadata ParseDatabase(string dbName, string connectionString)
        {
            var database = new DatabaseMetadata(dbName);

            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            // https://sqlite.org/pragma.html
            command.CommandText = @"SELECT m.name AS tableName, 
                                           p.*,
                                           i.*
                                    FROM sqlite_master m
                                    LEFT OUTER JOIN pragma_table_info(m.name) p
                                    LEFT OUTER JOIN pragma_index_info(m.name) i
                                         ON m.name <> p.name
                                    ORDER BY tableName, p.cid, p.name;";


            using (var reader = command.ExecuteReader())
            {
                database.Tables = new List<TableMetadata>();
                while (reader.Read())
                {
                    var table = database.Tables.SingleOrDefault(x => x.DbName == reader.GetString(0)) ?? new TableMetadata();
                    if (table.Database == null)
                    {
                        table.Database = database;
                        table.DbName = reader.GetString(0);
                        table.Type = TableType.Table; // TODO: Views
                        table.Model = new ModelMetadata
                        {
                            CsTypeName = table.DbName,
                            Table = table
                        };
                        table.Columns = new();
                        database.Tables.Add(table);
                    }

                    if (reader.IsDBNull(1))
                        continue;

                    var column = new Column()
                    {
                        DbName=reader.GetString(2),
                        DbType=reader.GetString(3),
                        Nullable=reader.GetBoolean(4)==false,
                        PrimaryKey=reader.GetBoolean(6),
                        /*
                         In SQLite, a column with type INTEGER PRIMARY KEY is an alias for the ROWID (except in WITHOUT ROWID tables) which is always a 64-bit signed integer.
                         On an INSERT, if the ROWID or INTEGER PRIMARY KEY column is not explicitly given a value, then it will be filled automatically with an unused integer, usually one more than the largest ROWID currently in use. 
                         This is true regardless of whether or not the AUTOINCREMENT keyword is used.
                         
                         https://www.sqlite.org/autoinc.html
                         */
                        AutoIncrement = reader.GetBoolean(6) && reader.GetString(3).ToLower() == "integer",
                        // https://www.sqlite.org/limits.html
                        // The current implementation will only support a string or BLOB length up to 231-1 or 2147483647
                        Length = 2147483647
                    };

                    var property = new Property();
                    property.CsTypeName = ParseCsType(column.DbType.ToLower());
                    property.CsNullable = column.Nullable && IsCsTypeNullable(property.CsTypeName);
                    column.ValueProperty = property;
                    table.Columns.Add(column);
                }
            }

            //foreach (var key in information_Schema
            //    .KEY_COLUMN_USAGE.Where(x => x.TABLE_SCHEMA == dbName && x.REFERENCED_COLUMN_NAME != null))
            //{
            //    var relation = new Relation
            //    {
            //        Constraint = key.CONSTRAINT_NAME,
            //        Type = RelationType.OneToMany
            //    };
            //
            //    var foreignKeyColumn = database
            //        .Tables.Single(x => x.DbName == key.TABLE_NAME)
            //        .Columns.Single(x => x.DbName == key.COLUMN_NAME);
            //
            //    var candidateKeyColumn = database
            //        .Tables.Single(x => x.DbName == key.REFERENCED_TABLE_NAME)
            //        .Columns.Single(x => x.DbName == key.REFERENCED_COLUMN_NAME);
            //
            //    relation.ForeignKey = CreateRelationPart(relation, foreignKeyColumn, RelationPartType.ForeignKey);
            //    relation.CandidateKey = CreateRelationPart(relation, candidateKeyColumn, RelationPartType.CandidateKey);
            //}

            database.Relations = database
                .Tables.SelectMany(x => x.Columns.SelectMany(y => y.RelationParts.Select(z => z.Relation)))
                .Distinct()
                .ToList();

            return database;
        }

        private static RelationPart CreateRelationPart(Relation relation, Column column, RelationPartType type)
        {
            var relationPart = new RelationPart
            {
                Relation = relation,
                Column = column,
                Type = type
            };

            column.RelationParts.Add(relationPart);

            return relationPart;
        }

        // https://www.sqlite.org/datatype3.html
        private static string ParseCsType(string dbType)
        {
            return dbType switch
            {
                "integer" => "int",
                "real" => "double",
                "text" => "string",
                "blob" => "byte[]",
                _ => throw new NotImplementedException($"Unknown type '{dbType}'"),
            };
        }

        private static bool IsCsTypeNullable(string csType)
        {
            return csType switch
            {
                "int" => true,
                "string" => false,
                "bool" => true,
                "double" => true,
                "DateTime" => true,
                "DateOnly" => true,
                "float" => true,
                "long" => true,
                "Guid" => true,
                "byte[]" => false,
                "decimal" => true,
                _ => throw new NotImplementedException($"Unknown type '{csType}'"),
            };
        }
    }
}