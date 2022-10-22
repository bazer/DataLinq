using DataLinq.Metadata;
using System;

namespace DataLinq.Attributes
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class TypeAttribute : Attribute
    {
        public TypeAttribute(string name)
        {
            DatabaseType = DatabaseType.MySQL;
            Name = name;
        }

        public TypeAttribute(string name, long length)
        {
            DatabaseType = DatabaseType.MySQL;
            Name = name;
            Length = length;
        }

        public TypeAttribute(string name, bool signed)
        {
            DatabaseType = DatabaseType.MySQL;
            Name = name;
            Signed = signed;
        }

        public TypeAttribute(string name, long length, bool signed)
        {
            DatabaseType = DatabaseType.MySQL;
            Name = name;
            Length = length;
            Signed = signed;
        }

        public TypeAttribute(DatabaseType databaseType, string name)
        {
            DatabaseType = databaseType;
            Name = name;
        }

        public TypeAttribute(DatabaseType databaseType, string name, long length)
        {
            DatabaseType = databaseType;
            Name = name;
            Length = length;
        }

        public TypeAttribute(DatabaseType databaseType, string name, bool signed)
        {
            DatabaseType = databaseType;
            Name = name;
            Signed = signed;
        }

        public TypeAttribute(DatabaseType databaseType, string name, long length, bool signed)
        {
            DatabaseType = databaseType;
            Name = name;
            Length = length;
            Signed = signed;
        }

        public TypeAttribute(DatabaseColumnType dbType)
        {
            DatabaseType = dbType.DatabaseType;
            Name = dbType.Name;
            Length = dbType.Length;
            Signed = dbType.Signed;
        }

        public long? Length { get; }
        public DatabaseType DatabaseType { get; }
        public string Name { get; }
        public bool? Signed { get; }
    }
}