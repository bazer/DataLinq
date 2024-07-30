using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using DataLinq.Extensions.Helpers;

namespace DataLinq.Metadata;

public class DatabaseColumnType
{
    public DatabaseType DatabaseType { get; set; }
    public string Name { get; set; }
    public long? Length { get; set; }
    public int? Decimals { get; set; }
    public bool? Signed { get; set; }

    public override string ToString()
    {
        return $"{Name} ({Length}) [{DatabaseType}]";
    }
}

public class Column
{
    public string DbName { get; set; }
    public DatabaseColumnType[] DbTypes { get; private set; } = [];
    public int Index { get; set; }
    public bool ForeignKey { get; set; }
    public bool PrimaryKey { get; private set; }
    public bool Unique => ColumnIndices.Any(x => x.Characteristic == Attributes.IndexCharacteristic.Unique);
    public bool AutoIncrement { get; set; }
    public bool Nullable { get; set; }
    //public List<RelationPart> RelationParts { get; set; } = new List<RelationPart>();
    public IEnumerable<ColumnIndex> ColumnIndices => Table.ColumnIndices.Where(x => x.Columns.Contains(this));
    public TableMetadata Table { get; set; }
    public ValueProperty ValueProperty { get; set; }

    public void SetPrimaryKey(bool value)
    {
        PrimaryKey = value;

        if (value)
            Table.AddPrimaryKeyColumn(this);
        else
            Table.RemovePrimaryKeyColumn(this);
    }

    public void AddDbType(DatabaseColumnType columnType)
    {
        DbTypes = DbTypes.AsEnumerable().Append(columnType).ToArray();
    }

    private ConcurrentDictionary<DatabaseType, DatabaseColumnType?> cachedDbTypes = new();
    public DatabaseColumnType? GetDbTypeFor(DatabaseType databaseType)
    {
        if (cachedDbTypes.TryGetValue(databaseType, out DatabaseColumnType? result))
            return result;
        else
            return cachedDbTypes.GetOrAdd(databaseType, type => DbTypes.FirstOrDefault(x => x.DatabaseType == type) ?? DbTypes.FirstOrDefault());
    }

    public override string ToString()
    {
        return $"{Table.DbName}.{DbName} ({DbTypes.ToJoinedString(", ")})";
    }
}