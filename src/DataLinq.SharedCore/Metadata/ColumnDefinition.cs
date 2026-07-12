using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Extensions.Helpers;
using DataLinq.Interfaces;
using System;
using System.Threading;

namespace DataLinq.Metadata;

public class DatabaseColumnType(DatabaseType databaseType, string name, ulong? length = null, uint? decimals = null, bool? signed = null)
{
    public DatabaseType DatabaseType { get; } = databaseType;
    public string Name { get; private set; } = name;
    public bool IsFrozen { get; private set; }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetName(string name)
    {
        SetNameCore(name);
    }

    internal void SetNameCore(string name)
    {
        ThrowIfFrozen();
        Name = name;
    }

    public ulong? Length { get; private set; } = length;

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetLength(ulong? length)
    {
        SetLengthCore(length);
    }

    internal void SetLengthCore(ulong? length)
    {
        ThrowIfFrozen();
        Length = length == 0 ? null : length;
    }

    public uint? Decimals { get; private set; } = decimals;

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetDecimals(uint? decimals)
    {
        SetDecimalsCore(decimals);
    }

    internal void SetDecimalsCore(uint? decimals)
    {
        ThrowIfFrozen();
        Decimals = decimals;
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetDecimals(ulong? decimals)
    {
        SetDecimalsCore(decimals);
    }

    internal void SetDecimalsCore(ulong? decimals)
    {
        ThrowIfFrozen();
        Decimals = (uint?)decimals;
    }

    public bool? Signed { get; private set; } = signed;

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void SetSigned(bool signed)
    {
        SetSignedCore(signed);
    }

    internal void SetSignedCore(bool signed)
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
    private MetadataCollection<DatabaseColumnType> dbTypes = MetadataCollection<DatabaseColumnType>.Empty;
    private MetadataCollection<GuidStorageDefinition> guidStorageDefinitions = MetadataCollection<GuidStorageDefinition>.Empty;
    private MetadataCollection<DatabaseType> unresolvedGuidStorageProviders = MetadataCollection<DatabaseType>.Empty;
    private ColumnScalarMapping scalarMapping = null!;

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

    public MetadataCollection<DatabaseColumnType> DbTypes => dbTypes;
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
    public bool Unique
    {
        get
        {
            var indices = Table.GetColumnIndices(this);
            for (var i = 0; i < indices.Count; i++)
            {
                if (indices[i].Characteristic == Attributes.IndexCharacteristic.Unique)
                    return true;
            }

            return false;
        }
    }
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

    public IEnumerable<ColumnIndex> ColumnIndices => Table.GetColumnIndices(this);
    public ValueProperty ValueProperty { get; private set; } = null!;
    public ColumnScalarMapping ScalarMapping => scalarMapping;
    public CsTypeDeclaration ModelCsType => scalarMapping.ModelCsType;
    public CsTypeDeclaration ProviderCsType => scalarMapping.ProviderCsType;
    public Type? ModelClrType => scalarMapping.ModelClrType;
    public Type? ProviderClrType => scalarMapping.ProviderClrType;
    public IDataLinqScalarConverter? ScalarConverter => scalarMapping.Converter;
    public bool HasScalarConverter => scalarMapping.HasConverter;
    /// <summary>
    /// Gets whether this column's resolved canonical provider CLR type is
    /// <see cref="Guid"/>.
    /// </summary>
    public bool IsGuidColumn
    {
        get
        {
            if (ProviderClrType is { } providerClrType)
                return (System.Nullable.GetUnderlyingType(providerClrType) ?? providerClrType) == typeof(Guid);

            return (string.Equals(ProviderCsType.Name, nameof(Guid), StringComparison.Ordinal) &&
                    string.Equals(ProviderCsType.Namespace, typeof(Guid).Namespace, StringComparison.Ordinal)) ||
                   string.Equals(ProviderCsType.Name, typeof(Guid).FullName, StringComparison.Ordinal);
        }
    }
    /// <summary>
    /// Gets the immutable UUID storage definitions resolved for concrete
    /// database providers.
    /// </summary>
    public MetadataCollection<GuidStorageDefinition> GuidStorageDefinitions => guidStorageDefinitions;
    internal MetadataCollection<DatabaseType> UnresolvedGuidStorageProviders => unresolvedGuidStorageProviders;

    /// <summary>
    /// Gets the UUID storage definition resolved for one concrete provider.
    /// The lookup is exact and does not fall back to
    /// <see cref="DatabaseType.Default"/>.
    /// </summary>
    /// <param name="databaseType">The concrete provider to look up.</param>
    /// <returns>The resolved definition, or <see langword="null"/>.</returns>
    public GuidStorageDefinition? GetGuidStorageFor(DatabaseType databaseType)
    {
        for (var i = 0; i < guidStorageDefinitions.Count; i++)
        {
            var definition = guidStorageDefinitions[i];
            if (definition.DatabaseType == databaseType)
                return definition;
        }

        return null;
    }

    /// <summary>
    /// Gets whether provider-imported metadata could not determine the UUID
    /// byte layout for the specified provider.
    /// </summary>
    /// <param name="databaseType">The concrete provider to inspect.</param>
    /// <returns>
    /// <see langword="true"/> when the provider is applicable but its UUID
    /// storage format remains unresolved; otherwise <see langword="false"/>.
    /// </returns>
    public bool IsGuidStorageUnresolvedFor(DatabaseType databaseType)
    {
        for (var i = 0; i < unresolvedGuidStorageProviders.Count; i++)
        {
            if (unresolvedGuidStorageProviders[i] == databaseType)
                return true;
        }

        return false;
    }

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
        scalarMapping = ColumnScalarMapping.Identity(value.CsType);
        value.SetColumnCore(this);
    }

    internal void SetScalarMappingCore(ColumnScalarMapping value)
    {
        ThrowIfFrozen();
        scalarMapping = value ?? throw new ArgumentNullException(nameof(value));
    }

    internal void SetGuidStorageDefinitionsCore(IEnumerable<GuidStorageDefinition> definitions)
    {
        ThrowIfFrozen();
        guidStorageDefinitions = new MetadataCollection<GuidStorageDefinition>(definitions);
    }

    internal void SetUnresolvedGuidStorageProvidersCore(IEnumerable<DatabaseType> databaseTypes)
    {
        ThrowIfFrozen();
        unresolvedGuidStorageProviders = new MetadataCollection<DatabaseType>(databaseTypes);
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
        dbTypes = new MetadataCollection<DatabaseColumnType>(dbTypes.Append(columnType));
        cachedDbTypes?.Clear();
    }

    internal void SetDbTypesCore(IEnumerable<DatabaseColumnType> columnTypes)
    {
        ThrowIfFrozen();
        dbTypes = new MetadataCollection<DatabaseColumnType>(columnTypes);
        cachedDbTypes?.Clear();
    }

    private ConcurrentDictionary<DatabaseType, DatabaseColumnType?>? cachedDbTypes;
    public DatabaseColumnType? GetDbTypeFor(DatabaseType databaseType)
    {
        var cache = cachedDbTypes;
        if (cache is not null && cache.TryGetValue(databaseType, out DatabaseColumnType? result))
            return result;

        if (cache is null)
        {
            var newCache = new ConcurrentDictionary<DatabaseType, DatabaseColumnType?>();
            cache = Interlocked.CompareExchange(ref cachedDbTypes, newCache, null) ?? newCache;
        }

        return cache.GetOrAdd(databaseType, type => dbTypes.FirstOrDefault(x => x.DatabaseType == type) ?? dbTypes.FirstOrDefault());
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
