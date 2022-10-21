using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.MySql.Models;

namespace DataLinq.MySql
{
    public static class MetadataFromSqlFactory
    {
        public static DatabaseMetadata ParseDatabase(string name, string dbName, information_schema information_Schema)
        {
            var database = new DatabaseMetadata(name, dbName);

            database.Tables = information_Schema
                .TABLES.Where(x => x.TABLE_SCHEMA == dbName)
                .AsEnumerable()
                .Select(x => ParseTable(database, information_Schema, x))
                .ToList();

            ParseRelations(database, information_Schema);
            ParseIndices(database, information_Schema);
            MetadataFromInterfaceFactory.ParseIndices(database);
            database.Relations = MetadataFromInterfaceFactory
                .ParseRelations(database)
                .ToList();




            //foreach (var key in information_Schema
            //    .KEY_COLUMN_USAGE.Where(x => x.TABLE_SCHEMA == dbName && x.REFERENCED_COLUMN_NAME != null))
            //{


            //    //var relation = new Relation
            //    //{
            //    //    ConstraintName = key.CONSTRAINT_NAME,
            //    //    Type = RelationType.OneToMany
            //    //};

            //    var foreignKeyColumn = database
            //        .Tables.Single(x => x.DbName == key.TABLE_NAME)
            //        .Columns.Single(x => x.DbName == key.COLUMN_NAME);

            //    foreignKeyColumn.ForeignKey = true;
            //    foreignKeyColumn.ValueProperty.Attributes.Add(new ForeignKeyAttribute(key.REFERENCED_TABLE_NAME, key.REFERENCED_COLUMN_NAME, key.CONSTRAINT_NAME));

            //    //var candidateKeyColumn = database
            //    //    .Tables.Single(x => x.DbName == key.REFERENCED_TABLE_NAME)
            //    //    .Columns.Single(x => x.DbName == key.REFERENCED_COLUMN_NAME);

            //    //relation.ForeignKey = CreateRelationPart(relation, foreignKeyColumn, RelationPartType.ForeignKey);
            //    //relation.CandidateKey = CreateRelationPart(relation, candidateKeyColumn, RelationPartType.CandidateKey);

            //    //MetadataFromInterfaceFactory.AttachRelationProperty(relation.ForeignKey, candidateKeyColumn);
            //    //MetadataFromInterfaceFactory.AttachRelationProperty(relation.CandidateKey, foreignKeyColumn);
            //}


            //foreach (var dbIndex in information_Schema
            //    .KEY_COLUMN_USAGE.Where(x => x.TABLE_SCHEMA == dbName && x.REFERENCED_COLUMN_NAME == null && x.CONSTRAINT_NAME != "PRIMARY").ToList().GroupBy(x => x.CONSTRAINT_NAME))
            //{
            //    var index = new ColumnIndex
            //    {
            //        ConstraintName = dbIndex.First().CONSTRAINT_NAME,
            //        Type = IndexType.Unique
            //    };

            //    var columns = dbIndex.Select(key => database
            //        .Tables.Single(x => x.DbName == key.TABLE_NAME)
            //        .Columns.Single(x => x.DbName == key.COLUMN_NAME));

            //    var tables = columns.GroupBy(x => x.Table);
            //    if (tables.Count() != 1)
            //        throw new Exception($"Constraint '{index.ConstraintName}' seems to be split over multiple tables.");

            //    var table = tables.Single().Key;
            //    if (table.ColumnIndices.Any(x => x.ConstraintName == index.ConstraintName))
            //        throw new Exception($"Constraint '{index.ConstraintName}' already added to table '{table.DbName}'.");

            //    index.Columns.AddRange(columns);
            //    table.ColumnIndices.Add(index);
            //}



            //database.Relations = database
            //    .Tables.SelectMany(x => x.Columns.SelectMany(y => y.RelationParts.Select(z => z.Relation)))
            //    .Distinct()
            //    .ToList();



            return database;
        }

        private static void ParseRelations(DatabaseMetadata database, information_schema information_Schema)
        {
            foreach (var key in information_Schema
                .KEY_COLUMN_USAGE.Where(x => x.TABLE_SCHEMA == database.DbName && x.REFERENCED_COLUMN_NAME != null))
            {
                var foreignKeyColumn = database
                    .Tables.Single(x => x.DbName == key.TABLE_NAME)
                    .Columns.Single(x => x.DbName == key.COLUMN_NAME);

                foreignKeyColumn.ForeignKey = true;
                foreignKeyColumn.ValueProperty.Attributes.Add(new ForeignKeyAttribute(key.REFERENCED_TABLE_NAME, key.REFERENCED_COLUMN_NAME, key.CONSTRAINT_NAME));
            }
        }

        private static void ParseIndices(DatabaseMetadata database, information_schema information_Schema)
        {
            foreach (var dbIndex in information_Schema
                .KEY_COLUMN_USAGE.Where(x => x.TABLE_SCHEMA == database.DbName && x.REFERENCED_COLUMN_NAME == null && x.CONSTRAINT_NAME != "PRIMARY").ToList().GroupBy(x => x.CONSTRAINT_NAME))
            {
                //var index = new ColumnIndex
                //{
                //    ConstraintName = dbIndex.First().CONSTRAINT_NAME,
                //    Type = IndexType.Unique
                //};

                var columns = dbIndex.Select(key => database
                    .Tables.Single(x => x.DbName == key.TABLE_NAME)
                    .Columns.Single(x => x.DbName == key.COLUMN_NAME));

                foreach (var column in columns)
                {
                    column.Unique = true;
                    column.ValueProperty.Attributes.Add(new UniqueAttribute(dbIndex.First().CONSTRAINT_NAME));
                }

                //var tables = columns.GroupBy(x => x.Table);
                //if (tables.Count() != 1)
                //    throw new Exception($"Constraint '{index.ConstraintName}' seems to be split over multiple tables.");

                //var table = tables.Single().Key;
                //if (table.ColumnIndices.Any(x => x.ConstraintName == index.ConstraintName))
                //    throw new Exception($"Constraint '{index.ConstraintName}' already added to table '{table.DbName}'.");

                //index.Columns.AddRange(columns);
                //table.ColumnIndices.Add(index);
            }
        }

        //private static RelationPart CreateRelationPart(Relation relation, Column column, RelationPartType type)
        //{
        //    var relationPart = new RelationPart
        //    {
        //        Relation = relation,
        //        Column = column,
        //        Type = type,
        //        CsName = column.Table.Model.CsTypeName
        //    };

        //    column.RelationParts.Add(relationPart);

        //    return relationPart;
        //}

        private static TableMetadata ParseTable(DatabaseMetadata database, information_schema information_Schema, TABLES dbTables)
        {
            var type = dbTables.TABLE_TYPE == "BASE TABLE" ? TableType.Table : TableType.View;

            var table = type == TableType.Table
                ? new TableMetadata()
                : new ViewMetadata();

            table.Database = database;
            table.DbName = dbTables.TABLE_NAME;
            table.Model = new ModelMetadata
            {
                CsTypeName = dbTables.TABLE_NAME,
                CsDatabasePropertyName = dbTables.TABLE_NAME,
                Table = table,
                Database = table.Database
            };

            if (table is ViewMetadata view)
            {
                view.Definition = information_Schema
                    .VIEWS.Where(x => x.TABLE_SCHEMA == database.DbName && x.TABLE_NAME == view.DbName)
                    .AsEnumerable()
                    .Select(x => x.VIEW_DEFINITION)
                    .FirstOrDefault()?
                    .Replace($"`{database.DbName}`.", "");
            }

            table.Columns = information_Schema
                .COLUMNS.Where(x => x.TABLE_SCHEMA == database.DbName && x.TABLE_NAME == table.DbName)
                .AsEnumerable()
                .Select(x => ParseColumn(table, x))
                .ToList();

            return table;
        }

        private static Column ParseColumn(TableMetadata table, COLUMNS dbColumns)
        {
            var column = new Column();

            column.Table = table;
            column.DbName = dbColumns.COLUMN_NAME;
            column.DbType = dbColumns.DATA_TYPE;
            column.Nullable = dbColumns.IS_NULLABLE == "YES";
            column.Length = dbColumns.CHARACTER_MAXIMUM_LENGTH;
            column.PrimaryKey = dbColumns.COLUMN_KEY == "PRI";
            column.AutoIncrement = dbColumns.EXTRA.Contains("auto_increment");
            column.Signed = dbColumns.COLUMN_TYPE.Contains("unsigned") ? false : null;

            //var property = new Property();
            //property.Column = column;
            //property.Model = table.Model;
            //property.CsName = column.DbName;
            //property.CsSize = MetadataTypeConverter.CsTypeSize(property.CsTypeName);
            //property.CsTypeName = MetadataTypeConverter.ParseCsType(column.DbType);
            //property.CsNullable = column.Nullable && MetadataTypeConverter.IsCsTypeNullable(property.CsTypeName);
            //property.Attributes = GetAttributes(property).ToList();

            column.ValueProperty = MetadataFromInterfaceFactory.ParseProperty(column);
            table.Model.Properties.Add(column.ValueProperty);

            return column;
        }

    }
}