using System;
using System.Linq;
using System.Reflection;
using Slim.Attributes;

namespace Slim.Metadata
{
    public static class MetadataFromInterfaceFactory
    {
        public static Database ParseDatabase(Type type)
        {
            //var type = databaseModel.GetType();
            var database = new Database(type.Name);

            foreach (var attribute in type.GetCustomAttributes(false))
            {
                if (attribute is NameAttribute)
                    database.Name = ((NameAttribute)attribute).Name;
            }

            database.Tables = type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(GetTableType)
                .Select(x => ParseTable(database, x))
                .ToList();

            return database;
        }

        private static Type GetTableType(PropertyInfo property)
        {
            var type = property.PropertyType;

            if (type.GetGenericTypeDefinition() == typeof(DbRead<>))
                return type.GetGenericArguments()[0];
            else
                throw new NotImplementedException();
        }

        private static Table ParseTable(Database database, Type type)
        {
            var table = new Table();
            table.Database = database;
            table.Name = type.Name;
            table.CsType = type;
            table.CsTypeName = type.Name;
            table.Type = type.GetInterfaces().Any(x => x.Name == "ITableModel")
                ? TableType.Table
                : TableType.View;

            foreach (var attribute in type.GetCustomAttributes(false))
            {
                if (attribute is NameAttribute)
                    table.Name = ((NameAttribute)attribute).Name;
            }

            table.Columns = type
                .GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(x => ParseColumn(table, x))
                .ToList();

            return table;
        }

        private static Column ParseColumn(Table table, PropertyInfo property)
        {
            var column = new Column();
            column.Table = table;
            column.Name = property.Name;
            column.CsType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            column.CsTypeName = GetKeywordName(column.CsType);
            column.CsNullable = property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>);
            column.Property = property;

            foreach (var attribute in property.GetCustomAttributes(false))
            {
                if (attribute is NameAttribute)
                    column.Name = ((NameAttribute)attribute).Name;

                if (attribute is NullableAttribute)
                    column.Nullable = true;

                if (attribute is PrimaryKeyAttribute)
                    column.PrimaryKey = true;

                if (attribute is TypeAttribute t)
                {
                    column.DbType = t.Name;
                    column.Length = t.Length;
                }
            }

            return column;
        }

        private static string GetKeywordName(Type type)
        {
            switch (type.Name)
            {
                case "Int32":
                    return "int";

                default:
                    return type.Name;
            }
        }
    }
}