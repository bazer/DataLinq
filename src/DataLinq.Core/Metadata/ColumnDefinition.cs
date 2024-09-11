using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using DataLinq.Extensions.Helpers;

namespace DataLinq.Metadata;

public class DefaultValue(DatabaseType databaseType, string value)
{
    public DatabaseType DatabaseType { get; } = databaseType;
    public string Value { get; } = value;

    public override string ToString()
    {
        return $"\"{Value}\" [{DatabaseType}]";
    }
}

public class DatabaseColumnType(DatabaseType databaseType, string name, long? length = null, int? decimals = null, bool? signed = null)
{
    public DatabaseType DatabaseType { get; } = databaseType;
    public string Name { get; private set; } = name;
    public void SetName(string name) => Name = name;
    public long? Length { get; private set; } = length;
    public void SetLength(long? length) => Length = length == 0 ? null : length;
    public int? Decimals { get; private set; } = decimals;
    public void SetDecimals(int? decimals) => Decimals = decimals;
    public void SetDecimals(long? decimals) => Decimals = (int?)decimals;
    public bool? Signed { get; private set; } = signed;
    public void SetSigned(bool signed) => Signed = signed;

    public override string ToString() => $"{Name} ({Length}) [{DatabaseType}]";

    public DatabaseColumnType Clone() => new(DatabaseType, Name, Length, Decimals, Signed);
}

public class ColumnDefinition(string dbName, TableDefinition table)
{
    public TableDefinition Table { get; } = table;
    public string DbName { get; private set; } = dbName;
    public void SetDbName(string value) => DbName = value;
    public DatabaseColumnType[] DbTypes { get; private set; } = [];
    public int Index { get; private set; }
    public bool ForeignKey { get; private set; }
    public void SetForeignKey(bool value = true) => ForeignKey = value;
    public bool PrimaryKey { get; private set; }
    public bool Unique => ColumnIndices.Any(x => x.Characteristic == Attributes.IndexCharacteristic.Unique);
    public bool AutoIncrement { get; private set; }
    public void SetAutoIncrement(bool value = true) => AutoIncrement = value;
    public bool Nullable { get; private set; }
    public void SetNullable(bool value = true) => Nullable = value;
    public DefaultValue[] DefaultValues { get; private set; } = [];
    
    public IEnumerable<ColumnIndex> ColumnIndices => Table.ColumnIndices.Where(x => x.Columns.Contains(this));
    public ValueProperty ValueProperty { get; private set; }
    public void SetValueProperty(ValueProperty value)
    {
        ValueProperty = value;
        value.SetColumn(this);
    }

    public void SetPrimaryKey(bool value = true)
    {
        PrimaryKey = value;

        if (value)
            Table.AddPrimaryKeyColumn(this);
        else
            Table.RemovePrimaryKeyColumn(this);
    }

    public void AddDefaultValue(DatabaseType dbType, string value) => AddDefaultValue(new(dbType, value));
    public void AddDefaultValue(DefaultValue defaultValue) => DefaultValues = DefaultValues.AsEnumerable().Append(defaultValue).ToArray();

    public void AddDbType(DatabaseColumnType columnType)
    {
        DbTypes = DbTypes.AsEnumerable().Append(columnType).ToArray();
    }

    private readonly ConcurrentDictionary<DatabaseType, DatabaseColumnType?> cachedDbTypes = new();
    public DatabaseColumnType? GetDbTypeFor(DatabaseType databaseType)
    {
        if (cachedDbTypes.TryGetValue(databaseType, out DatabaseColumnType? result))
            return result;
        else
            return cachedDbTypes.GetOrAdd(databaseType, type => DbTypes.FirstOrDefault(x => x.DatabaseType == type) ?? DbTypes.FirstOrDefault());
    }

    public override string ToString() => $"{Table.DbName}.{DbName} ({DbTypes.ToJoinedString(", ")})";
}