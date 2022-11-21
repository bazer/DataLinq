using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using DataLinq.Attributes;
using DataLinq.Metadata;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite
{
    public class MetadataFromSqlFactory
    {
        private readonly MetadataFromSqlFactoryOptions options;
        private SQLiteDbAccess dbAccess;

        public MetadataFromSqlFactory(MetadataFromSqlFactoryOptions options)
        {
            this.options = options;
        }

        public DatabaseMetadata ParseDatabase(string name, string csTypeName, string dbName, string connectionString)
        {
            dbAccess = new SQLiteDbAccess(connectionString, Mutation.TransactionType.NoTransaction);

            var database = new DatabaseMetadata(name, null, csTypeName, dbName);
            database.TableModels = dbAccess
                .ReadReader("SELECT *\r\nFROM sqlite_master m\r\nWHERE\r\nm.type <> 'index'")
                .Select(x => ParseTable(database, x))
                .ToList();

            //ParseIndices(database, information_Schema);
            ParseRelations(database);

            MetadataFactory.ParseIndices(database);
            MetadataFactory.ParseRelations(database);


            //using var connection = new SqliteConnection(connectionString);
            //connection.Open();
            //var command = connection.CreateCommand();
            //// https://sqlite.org/pragma.html
            //command.CommandText = @"SELECT m.name AS tableName, 
            //                               t.*,
            //                               i.*,
            //                               m.type
            //                        FROM sqlite_master m
            //                        LEFT OUTER JOIN pragma_table_info(m.name) t
            //                        LEFT OUTER JOIN pragma_index_info(m.name) i
            //                             ON m.name <> t.name
            //                        ORDER BY tableName, t.cid, t.name;";


            //using (var reader = command.ExecuteReader())
            //{
            //    database.TableModels = new List<TableModelMetadata>();
            //    while (reader.Read())
            //    {
            //        var table = database.TableModels.SingleOrDefault(x => x.Table.DbName == reader.GetString(0))?.Table ?? (reader.GetString(10) == "table"
            //            ? new TableMetadata()
            //            : new ViewMetadata());

            //        //var table = database.Tables.SingleOrDefault(x => x.DbName == reader.GetString(0)) ?? new TableMetadata();
            //        if (table.Database == null)
            //        {
            //            table.Database = database;
            //            table.DbName = reader.GetString(0);
            //            if (table.DbName.StartsWith("sqlite_autoindex_"))
            //                continue;

            //            //table.Type = reader.GetString(10) == "table" ? TableType.Table : TableType.View;
            //            table.Model = new ModelMetadata
            //            {
            //                CsTypeName = table.DbName,
            //                Table = table
            //            };
            //            table.Columns = new();
            //            database.TableModels.Add(new TableModelMetadata
            //            {
            //                Table = table,
            //                Model = table.Model,
            //                CsPropertyName = table.Model.CsTypeName
            //            });
            //        }

            //        if (reader.IsDBNull(1))
            //            continue;

            //        var column = new Column()
            //        {
            //            DbName = reader.GetString(2),
            //            //DbTypes = reader.GetString(3).ToLower(),
            //            Index = reader.GetInt32(1),
            //            Table = table,
            //            Nullable = reader.GetBoolean(4) == false,
            //            PrimaryKey = reader.GetBoolean(6),
            //            //Signed = null,
            //            /*
            //             In SQLite, a column with type INTEGER PRIMARY KEY is an alias for the ROWID (except in WITHOUT ROWID tables) which is always a 64-bit signed integer.
            //             On an INSERT, if the ROWID or INTEGER PRIMARY KEY column is not explicitly given a value, then it will be filled automatically with an unused integer, usually one more than the largest ROWID currently in use. 
            //             This is true regardless of whether or not the AUTOINCREMENT keyword is used.

            //             https://www.sqlite.org/autoinc.html
            //             */
            //            AutoIncrement = reader.GetBoolean(6) && reader.GetString(3).ToLower() == "integer",
            //            // https://www.sqlite.org/limits.html
            //            // The current implementation will only support a string or BLOB length up to 231-1 or 2147483647
            //            //Length = 2147483647
            //        };

            //        var dbType = new DatabaseColumnType
            //        {
            //            DatabaseType = DatabaseType.SQLite,
            //            Name = reader.GetString(3).ToLower(),
            //            Length = 2147483647,
            //            Signed = null
            //        };

            //        if (string.IsNullOrEmpty(dbType.Name))
            //            dbType.Name = "text";

            //        column.DbTypes.Add(dbType);
            //        MetadataFactory.AttachValueProperty(column, ParseCsType(dbType.Name), false);

            //        //var property = new ValueProperty();
            //        //property.CsTypeName = ParseCsType(dbType.Name.ToLower());
            //        //property.CsNullable = column.Nullable && IsCsTypeNullable(property.CsTypeName);
            //        //column.ValueProperty = property;

            //        table.Columns.Add(column);
            //    }
            //}

            //// https://sqlite.org/foreignkeys.html
            //foreach (var table in database.TableModels)
            //{
            //    command = connection.CreateCommand();
            //    command.CommandText = $"SELECT `id`, `table`, `from`, `to` FROM pragma_foreign_key_list('{table.Table.DbName}')";
            //    using var reader = command.ExecuteReader();
            //    while (reader.Read())
            //    {
            //        var relation = new Relation
            //        {
            //            ConstraintName = reader.GetString(0),
            //            Type = RelationType.OneToMany
            //        };

            //        var foreignKeyColumn = database
            //            .TableModels.Single(x => x.Table.DbName == table.Table.DbName)
            //            .Table.Columns.Single(x => x.DbName == reader.GetString(2));

            //        var candidateKeyColumn = database
            //            .TableModels.Single(x => x.Table.DbName == reader.GetString(1))
            //            .Table.Columns.Single(x => x.DbName == reader.GetString(3));

            //        relation.ForeignKey = CreateRelationPart(relation, foreignKeyColumn, RelationPartType.ForeignKey);
            //        relation.CandidateKey = CreateRelationPart(relation, candidateKeyColumn, RelationPartType.CandidateKey);
            //    }
            //}

            //database.Relations = database
            //    .Tables.SelectMany(x => x.Columns.SelectMany(y => y.RelationParts.Select(z => z.Relation)))
            //    .Distinct()
            //    .ToList();

            return database;
        }

        //private void ParseIndices(DatabaseMetadata database, IDataLinqDataReader reader)
        //{
        //    foreach (var dbIndex in information_Schema
        //        .KEY_COLUMN_USAGE.Where(x => x.TABLE_SCHEMA == database.DbName && x.REFERENCED_COLUMN_NAME == null && x.CONSTRAINT_NAME != "PRIMARY").ToList().GroupBy(x => x.CONSTRAINT_NAME))
        //    {
        //        var columns = dbIndex.Select(key => database
        //            .TableModels.Single(x => x.Table.DbName == key.TABLE_NAME)
        //            .Table.Columns.Single(x => x.DbName == key.COLUMN_NAME));

        //        foreach (var column in columns)
        //        {
        //            column.Unique = true;
        //            column.ValueProperty.Attributes.Add(new UniqueAttribute(dbIndex.First().CONSTRAINT_NAME));
        //        }
        //    }
        //}

        private void ParseRelations(DatabaseMetadata database)
        {
            foreach (var tableModel in database.TableModels.Where(x => x.Table.Type == TableType.Table))
            {
                foreach (var reader in dbAccess.ReadReader($"SELECT `id`, `table`, `from`, `to` FROM pragma_foreign_key_list('{tableModel.Table.DbName}')"))
                {
                    var foreignKeyColumn = tableModel
                        .Table.Columns.Single(x => x.DbName == reader.GetString(2));

                    //var foreignKeyColumn = database
                    //    .TableModels.Single(x => x.Table.DbName == tableModel.Table.DbName)
                    //    .Table.Columns.Single(x => x.DbName == reader.GetString(2));

                    foreignKeyColumn.ForeignKey = true;
                    foreignKeyColumn.ValueProperty.Attributes.Add(new ForeignKeyAttribute(reader.GetString(1), reader.GetString(3), reader.GetString(0)));
                }
            }

            //command = connection.CreateCommand();
            //command.CommandText = $"SELECT `id`, `table`, `from`, `to` FROM pragma_foreign_key_list('{table.Table.DbName}')";
            //using var reader = command.ExecuteReader();
            //while (reader.Read())
            //{
            //    var relation = new Relation
            //    {
            //        ConstraintName = reader.GetString(0),
            //        Type = RelationType.OneToMany
            //    };

            //    var foreignKeyColumn = database
            //        .TableModels.Single(x => x.Table.DbName == table.Table.DbName)
            //        .Table.Columns.Single(x => x.DbName == reader.GetString(2));

            //    var candidateKeyColumn = database
            //        .TableModels.Single(x => x.Table.DbName == reader.GetString(1))
            //        .Table.Columns.Single(x => x.DbName == reader.GetString(3));

            //    relation.ForeignKey = CreateRelationPart(relation, foreignKeyColumn, RelationPartType.ForeignKey);
            //    relation.CandidateKey = CreateRelationPart(relation, candidateKeyColumn, RelationPartType.CandidateKey);
            //}
        }

        private TableModelMetadata ParseTable(DatabaseMetadata database, IDataLinqDataReader reader)
        {
            var type = reader.GetString(0) == "table" ? TableType.Table : TableType.View; //sqlite_master.type
            var table = type == TableType.Table
                 ? new TableMetadata()
                 : new ViewMetadata();

            table.Database = database;
            table.DbName = reader.GetString(2); //sqlite_master.tbl_name
            MetadataFactory.AttachModel(table, options.CapitaliseNames);

            if (table is ViewMetadata view)
            {
                view.Definition = reader.GetString(4) //sqlite_master.sql
                    .ReplaceLineEndings(" ")
                    .Replace("\"", @"\""");
            }

            table.Columns = dbAccess
                .ReadReader($"SELECT * FROM pragma_table_info(\"{table.DbName}\")")
                .Select(x => ParseColumn(table, x))
                .ToList();

            return new TableModelMetadata
            {
                Table = table,
                Model = table.Model,
                CsPropertyName = table.Model.CsTypeName
            };
        }

        private Column ParseColumn(TableMetadata table, IDataLinqDataReader reader)
        {
            var dbType = new DatabaseColumnType
            {
                DatabaseType = DatabaseType.SQLite,
                Name = reader.GetString(2),
                Length = 2147483647,
                Signed = null
            };

            //For whatever reason, sometimes the data type for columns in views return ""
            if (string.IsNullOrEmpty(dbType.Name))
                dbType.Name = "text";

            var dbName = reader.GetString(1);

            var column = new Column
            {
                Table = table,
                DbName = dbName,
                Nullable = reader.GetBoolean(3) == false,
                PrimaryKey = reader.GetBoolean(5),
                AutoIncrement = dbAccess.ExecuteScalar<long>($"SELECT COUNT(*) FROM sqlite_sequence WHERE name='{dbName}'") > 0
            };

            column.DbTypes.Add(dbType);

            var csType = ParseCsType(dbType.Name);

            MetadataFactory.AttachValueProperty(column, csType, options.CapitaliseNames);

            return column;
        }


        private RelationPart CreateRelationPart(Relation relation, Column column, RelationPartType type)
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
        private string ParseCsType(string dbType)
        {
            return dbType.ToLower() switch
            {
                "integer" => "int",
                "real" => "double",
                "text" => "string",
                "blob" => "byte[]",
                _ => throw new NotImplementedException($"Unknown type '{dbType}'"),
            };
        }

        //private static bool IsCsTypeNullable(string csType)
        //{
        //    return csType switch
        //    {
        //        "int" => true,
        //        "string" => false,
        //        "bool" => true,
        //        "double" => true,
        //        "DateTime" => true,
        //        "DateOnly" => true,
        //        "float" => true,
        //        "long" => true,
        //        "Guid" => true,
        //        "byte[]" => false,
        //        "decimal" => true,
        //        _ => throw new NotImplementedException($"Unknown type '{csType}'"),
        //    };
        //}
    }
}