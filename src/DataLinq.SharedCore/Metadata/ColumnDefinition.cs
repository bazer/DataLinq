using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Extensions.Helpers;
using DataLinq.Interfaces;
using System;

namespace DataLinq.Metadata;

public class DatabaseColumnType(DatabaseType databaseType, string name, ulong? length = null, uint? decimals = null, bool? signed = null)
{
    public DatabaseType DatabaseType { get; } = databaseType;
    public string Name { get; private set; } = name;
    public bool IsFrozen { get; private set; }

    public void SetName(string name)
    {
        ThrowIfFrozen();
        Name = name;
    }

    public ulong? Length { get; private set; } = length;

    public void SetLength(ulong? length)
    {
        ThrowIfFrozen();
        Length = length == 0 ? null : length;
    }

    public uint? Decimals { get; private set; } = decimals;

    public void SetDecimals(uint? decimals)
    {
        ThrowIfFrozen();
        Decimals = decimals;
    }

    public void SetDecimals(ulong? decimals)
    {
        ThrowIfFrozen();
        Decimals = (uint?)decimals;
    }

    public bool? Signed { get; private set; } = signed;

    public void SetSigned(bool signed)
    {
        ThrowIfFrozen();
        Signed = signed;
    }

    public override string ToString() => $"{Name} ({Length}) [{DatabaseType}]";

    public DatabaseColumnType Clone() => new(DatabaseType, Name, Length, Decimals, Signed);
    public DatabaseColumnType Mutate(DatabaseType databaseType) => new(databaseType, Name, Length, Decimals, Signed);

    internal void Freeze()
    {
        if (IsFrozen)
            return;

        IsFrozen = true;
    }

    private void ThrowIfFrozen() => MetadataMutationGuard.ThrowIfFrozen(IsFrozen, this);
}

public class ColumnDefinition(string dbName, TableDefinition table) : IDefinition
{
    private DatabaseColumnType[] dbTypes = [];

    public TableDefinition Table { get; } = table;
    public string DbName { get; private set; } = dbName;
    public bool IsFrozen { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetDbName(string value)
    {
        SetDbNameCore(value);
    }

    internal void SetDbNameCore(string value)
    {
        ThrowIfFrozen();
        DbName = value;
    }

    public DatabaseColumnType[] DbTypes => dbTypes.ToArray();
    public int Index { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetIndex(int index)
    {
        SetIndexCore(index);
    }

    internal void SetIndexCore(int index)
    {
        ThrowIfFrozen();
        Index = index;
    }

    public bool ForeignKey { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetForeignKey(bool value = true)
    {
        SetForeignKeyCore(value);
    }

    internal void SetForeignKeyCore(bool value = true)
    {
        ThrowIfFrozen();
        ForeignKey = value;
    }

    public bool PrimaryKey { get; private set; }
    public bool Unique => ColumnIndices.Any(x => x.Characteristic == Attributes.IndexCharacteristic.Unique);
    public bool AutoIncrement { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetAutoIncrement(bool value = true)
    {
        SetAutoIncrementCore(value);
    }

    internal void SetAutoIncrementCore(bool value = true)
    {
        ThrowIfFrozen();
        AutoIncrement = value;
    }

    public bool Nullable { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetNullable(bool value = true)
    {
        SetNullableCore(value);
    }

    internal void SetNullableCore(bool value = true)
    {
        ThrowIfFrozen();
        Nullable = value;
    }

    public IEnumerable<ColumnIndex> ColumnIndices => Table.ColumnIndices.Where(x => x.Columns.Contains(this));
    public ValueProperty ValueProperty { get; private set; }

    public CsFileDeclaration? CsFile => Table?.Model?.CsFile;

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetValueProperty(ValueProperty value)
    {
        SetValuePropertyCore(value);
    }

    internal void SetValuePropertyCore(ValueProperty value)
    {
        ThrowIfFrozen();
        ValueProperty = value;
        value.SetColumnCore(this);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetPrimaryKey(bool value = true)
    {
        SetPrimaryKeyCore(value);
    }

    internal void SetPrimaryKeyCore(bool value = true)
    {
        ThrowIfFrozen();
        PrimaryKey = value;

        if (value)
            Table.AddPrimaryKeyColumnCore(this);
        else
            Table.RemovePrimaryKeyColumnCore(this);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void AddDbType(DatabaseColumnType columnType)
    {
        AddDbTypeCore(columnType);
    }

    internal void AddDbTypeCore(DatabaseColumnType columnType)
    {
        ThrowIfFrozen();
        dbTypes = dbTypes.AsEnumerable().Append(columnType).ToArray();
    }

    private readonly ConcurrentDictionary<DatabaseType, DatabaseColumnType?> cachedDbTypes = new();
    public DatabaseColumnType? GetDbTypeFor(DatabaseType databaseType)
    {
        if (cachedDbTypes.TryGetValue(databaseType, out DatabaseColumnType? result))
            return result;
        else
            return cachedDbTypes.GetOrAdd(databaseType, type => dbTypes.FirstOrDefault(x => x.DatabaseType == type) ?? dbTypes.FirstOrDefault());
    }

    public override string ToString() => $"{Table.DbName}.{DbName} ({dbTypes.ToJoinedString(", ")})";

    internal void Freeze()
    {
        if (IsFrozen)
            return;

        IsFrozen = true;

        foreach (var dbType in dbTypes)
            dbType.Freeze();
    }

    private void ThrowIfFrozen() => MetadataMutationGuard.ThrowIfFrozen(IsFrozen, this);
}
