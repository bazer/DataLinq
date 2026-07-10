using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class ModelMaterializationServicesTests
{
    [Test]
    public async Task GetOrMaterialize_CacheMissUsesCanonicalConvertedPrimaryKey()
    {
        var converter = new RecordingScalarConverter(static value => new MaterializedId((int)value!));
        var table = CreateConvertedKeyTable(converter);
        var providerRow = CanonicalProviderValueRow.Create(table, new object?[] { 42 });
        var runtime = new RecordingRuntime();
        var services = new ModelMaterializationServices("sql", runtime);

        var materialized = services.GetOrMaterialize(providerRow);

        await Assert.That(materialized).IsSameReferenceAs(runtime.LastCreatedInstance!);
        await Assert.That(runtime.LastLookupKey!.Value.GetValue(0)).IsTypeOf<int>();
        await Assert.That(runtime.LastLookupKey.Value.GetValue(0)).IsEqualTo(42);
        await Assert.That(runtime.LastCacheKey!.Value.GetValue(0)).IsTypeOf<int>();
        await Assert.That(runtime.LastCacheKey.Value.GetValue(0)).IsEqualTo(42);
        await Assert.That(runtime.LastCacheRow![0]).IsTypeOf<MaterializedId>();
        await Assert.That(((MaterializedId)runtime.LastCacheRow[0]!).Value).IsEqualTo(42);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(1);
        await Assert.That(runtime.FactoryCalls).IsEqualTo(1);
        await Assert.That(runtime.CacheHitMetrics).IsEqualTo(0);
        await Assert.That(runtime.CacheMissMetrics).IsEqualTo(1);
        await Assert.That(runtime.MaterializationMetrics).IsEqualTo(1);
        await Assert.That(runtime.CacheInsertionMetrics).IsEqualTo(1);
    }

    [Test]
    public async Task GetOrMaterialize_WarmHitSkipsConversionFactoryAndMetrics()
    {
        var converter = new RecordingScalarConverter(static value => new MaterializedId((int)value!));
        var table = CreateConvertedKeyTable(converter);
        var providerRow = CanonicalProviderValueRow.Create(table, new object?[] { 42 });
        var cached = new TestImmutableInstance();
        var runtime = new RecordingRuntime();
        runtime.Seed(DataLinqKey.FromValue(42), cached);
        var services = new ModelMaterializationServices("sql", runtime);

        var materialized = services.GetOrMaterialize(providerRow);

        await Assert.That(materialized).IsSameReferenceAs(cached);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
        await Assert.That(runtime.CacheLookupCalls).IsEqualTo(1);
        await Assert.That(runtime.CacheHitMetrics).IsEqualTo(1);
        await Assert.That(runtime.CacheMissMetrics).IsEqualTo(0);
        await Assert.That(runtime.FactoryCalls).IsEqualTo(0);
        await Assert.That(runtime.CacheAddCalls).IsEqualTo(0);
        await Assert.That(runtime.MaterializationMetrics).IsEqualTo(0);
        await Assert.That(runtime.CacheInsertionMetrics).IsEqualTo(0);
    }

    [Test]
    public async Task GetOrMaterialize_DuplicateInsertionReturnsCachedWinner()
    {
        var converter = new RecordingScalarConverter(static value => new MaterializedId((int)value!));
        var table = CreateConvertedKeyTable(converter);
        var providerRow = CanonicalProviderValueRow.Create(table, new object?[] { 42 });
        var winner = new TestImmutableInstance();
        var runtime = new RecordingRuntime { CompetingWinner = winner };
        var services = new ModelMaterializationServices("sql", runtime);

        var materialized = services.GetOrMaterialize(providerRow);

        await Assert.That(materialized).IsSameReferenceAs(winner);
        await Assert.That(materialized).IsNotSameReferenceAs(runtime.LastCreatedInstance!);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(1);
        await Assert.That(runtime.CacheLookupCalls).IsEqualTo(1);
        await Assert.That(runtime.CacheHitMetrics).IsEqualTo(0);
        await Assert.That(runtime.CacheMissMetrics).IsEqualTo(1);
        await Assert.That(runtime.FactoryCalls).IsEqualTo(1);
        await Assert.That(runtime.CacheAddCalls).IsEqualTo(1);
        await Assert.That(runtime.MaterializationMetrics).IsEqualTo(1);
        await Assert.That(runtime.CacheInsertionMetrics).IsEqualTo(0);
    }

    [Test]
    public async Task GetOrMaterialize_RejectedCacheInsertionReturnsCreatedWithoutStoreMetric()
    {
        var converter = new RecordingScalarConverter(static value => new MaterializedId((int)value!));
        var table = CreateConvertedKeyTable(converter);
        var providerRow = CanonicalProviderValueRow.Create(table, new object?[] { 42 });
        var runtime = new RecordingRuntime { AcceptCacheAdds = false };
        var services = new ModelMaterializationServices("memory", runtime);

        var materialized = services.GetOrMaterialize(providerRow);

        await Assert.That(materialized).IsSameReferenceAs(runtime.LastCreatedInstance!);
        await Assert.That(runtime.CacheLookupCalls).IsEqualTo(1);
        await Assert.That(runtime.CacheHitMetrics).IsEqualTo(0);
        await Assert.That(runtime.CacheMissMetrics).IsEqualTo(1);
        await Assert.That(runtime.FactoryCalls).IsEqualTo(1);
        await Assert.That(runtime.CacheAddCalls).IsEqualTo(1);
        await Assert.That(runtime.MaterializationMetrics).IsEqualTo(1);
        await Assert.That(runtime.CacheInsertionMetrics).IsEqualTo(0);
    }

    [Test]
    public async Task GetOrMaterialize_ConversionAndFactoryFailuresDoNotRecordSuccessMetrics()
    {
        var conversionFailure = new InvalidOperationException("conversion failed");
        var throwingConverter = new RecordingScalarConverter(_ => throw conversionFailure);
        var conversionTable = CreateConvertedKeyTable(throwingConverter);
        var conversionRow = CanonicalProviderValueRow.Create(conversionTable, new object?[] { 42 });
        var conversionRuntime = new RecordingRuntime();
        var conversionServices = new ModelMaterializationServices("sql", conversionRuntime);

        var wrappedFailure = Capture<ProviderValueMaterializationException>(() =>
            conversionServices.GetOrMaterialize(conversionRow));

        await Assert.That(wrappedFailure.InnerException).IsSameReferenceAs(conversionFailure);
        await Assert.That(conversionRuntime.FactoryCalls).IsEqualTo(0);
        await Assert.That(conversionRuntime.CacheAddCalls).IsEqualTo(0);
        await Assert.That(conversionRuntime.CacheMissMetrics).IsEqualTo(1);
        await Assert.That(conversionRuntime.MaterializationMetrics).IsEqualTo(0);
        await Assert.That(conversionRuntime.CacheInsertionMetrics).IsEqualTo(0);

        var successfulConverter = new RecordingScalarConverter(static value => new MaterializedId((int)value!));
        var factoryTable = CreateConvertedKeyTable(successfulConverter);
        var factoryRow = CanonicalProviderValueRow.Create(factoryTable, new object?[] { 42 });
        var factoryFailure = new InvalidOperationException("factory failed");
        var factoryRuntime = new RecordingRuntime { FactoryException = factoryFailure };
        var factoryServices = new ModelMaterializationServices("sql", factoryRuntime);

        var thrownFactoryFailure = Capture<InvalidOperationException>(() =>
            factoryServices.GetOrMaterialize(factoryRow));

        await Assert.That(thrownFactoryFailure).IsSameReferenceAs(factoryFailure);
        await Assert.That(successfulConverter.FromProviderCalls).IsEqualTo(1);
        await Assert.That(factoryRuntime.FactoryCalls).IsEqualTo(1);
        await Assert.That(factoryRuntime.CacheAddCalls).IsEqualTo(0);
        await Assert.That(factoryRuntime.CacheMissMetrics).IsEqualTo(1);
        await Assert.That(factoryRuntime.MaterializationMetrics).IsEqualTo(0);
        await Assert.That(factoryRuntime.CacheInsertionMetrics).IsEqualTo(0);
    }

    [Test]
    public async Task GetOrMaterialize_NoPrimaryKeySkipsAllCacheOperations()
    {
        var table = CreateKeylessView();
        var providerRow = CanonicalProviderValueRow.Create(table, new object?[] { 42 });
        var runtime = new RecordingRuntime();
        var services = new ModelMaterializationServices("memory", runtime);

        var materialized = services.GetOrMaterialize(providerRow);

        await Assert.That(materialized).IsSameReferenceAs(runtime.LastCreatedInstance!);
        await Assert.That(runtime.CacheLookupCalls).IsEqualTo(0);
        await Assert.That(runtime.CacheHitMetrics).IsEqualTo(0);
        await Assert.That(runtime.CacheMissMetrics).IsEqualTo(0);
        await Assert.That(runtime.FactoryCalls).IsEqualTo(1);
        await Assert.That(runtime.CacheAddCalls).IsEqualTo(0);
        await Assert.That(runtime.MaterializationMetrics).IsEqualTo(1);
        await Assert.That(runtime.CacheInsertionMetrics).IsEqualTo(0);
    }

    [Test]
    public async Task GetOrMaterialize_CompositeBinaryKeyOwnsCanonicalSnapshot()
    {
        var converter = new RecordingScalarConverter(static value => new MaterializedId((int)value!));
        var table = CreateConvertedKeyTable(converter, includeBinaryKey: true);
        var binaryColumn = table.GetColumnByDbName("binary_key");
        var inputBytes = new byte[] { 1, 2, 3 };
        var providerRow = CanonicalProviderValueRow.Create(table, new object?[] { 42, inputBytes });
        inputBytes[0] = 9;
        var borrowedProviderBytes = (byte[])providerRow[binaryColumn]!;
        borrowedProviderBytes[1] = 9;
        var runtime = new RecordingRuntime();
        var services = new ModelMaterializationServices("sql", runtime);

        services.GetOrMaterialize(providerRow);
        var modelBytes = (byte[])runtime.LastCacheRow![binaryColumn]!;
        modelBytes[2] = 9;

        await Assert.That(runtime.LastCacheKey!.Value.ValueCount).IsEqualTo(2);
        await Assert.That(runtime.LastCacheKey.Value.GetValue(0)).IsEqualTo(42);
        await Assert.That((byte[])runtime.LastCacheKey.Value.GetValue(1)!).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(converter.FromProviderCalls).IsEqualTo(1);
    }

    [Test]
    public async Task GetOrMaterialize_NullPrimaryKeyComponentFailsBeforeLookupOrConversion()
    {
        var converter = new RecordingScalarConverter(static value => new MaterializedId((int)value!));
        var table = CreateConvertedKeyTable(
            converter,
            includeBinaryKey: true,
            binaryKeyNullable: true);
        var providerRow = CanonicalProviderValueRow.Create(table, new object?[] { 42, null });
        var runtime = new RecordingRuntime();
        var services = new ModelMaterializationServices("memory", runtime);

        var exception = Capture<InvalidOperationException>(() =>
            services.GetOrMaterialize(providerRow));

        await Assert.That(exception.Message).Contains("null primary-key component 'binary_key'");
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
        await Assert.That(runtime.CacheLookupCalls).IsEqualTo(0);
        await Assert.That(runtime.FactoryCalls).IsEqualTo(0);
        await Assert.That(runtime.MaterializationMetrics).IsEqualTo(0);
        await Assert.That(runtime.CacheInsertionMetrics).IsEqualTo(0);
    }

    [Test]
    public async Task GetOrMaterialize_RejectsDefaultCachePublicationResult()
    {
        var converter = new RecordingScalarConverter(static value => new MaterializedId((int)value!));
        var table = CreateConvertedKeyTable(converter);
        var providerRow = CanonicalProviderValueRow.Create(table, new object?[] { 42 });
        var runtime = new RecordingRuntime { ReturnDefaultPublication = true };
        var services = new ModelMaterializationServices("sql", runtime);

        var exception = Capture<InvalidOperationException>(() =>
            services.GetOrMaterialize(providerRow));

        await Assert.That(exception.Message).Contains("invalid cache publication result");
        await Assert.That(converter.FromProviderCalls).IsEqualTo(1);
        await Assert.That(runtime.FactoryCalls).IsEqualTo(1);
        await Assert.That(runtime.MaterializationMetrics).IsEqualTo(1);
        await Assert.That(runtime.CacheInsertionMetrics).IsEqualTo(0);
    }

    private static TableDefinition CreateConvertedKeyTable(
        RecordingScalarConverter converter,
        bool includeBinaryKey = false,
        bool binaryKeyNullable = false)
    {
        var properties = new List<MetadataValuePropertyDraft>
        {
            new(
                "Id",
                new CsTypeDeclaration(typeof(MaterializedId)),
                new MetadataColumnDraft("id") { PrimaryKey = true })
            {
                ScalarConverter = CreateConverterDraft(converter)
            }
        };

        if (includeBinaryKey)
        {
            properties.Add(
                new MetadataValuePropertyDraft(
                    "BinaryKey",
                    new CsTypeDeclaration(typeof(byte[])),
                    new MetadataColumnDraft("binary_key")
                    {
                        PrimaryKey = true,
                        Nullable = binaryKeyNullable
                    })
                {
                    CsNullable = binaryKeyNullable
                });
        }

        return BuildTable(
            new MetadataModelDraft(new CsTypeDeclaration(typeof(MaterializationRowModel)))
            {
                ValueProperties = properties
            },
            new MetadataTableDraft("materialization_rows"));
    }

    private static TableDefinition CreateKeylessView() =>
        BuildTable(
            new MetadataModelDraft(new CsTypeDeclaration(typeof(MaterializationRowModel)))
            {
                OriginalInterfaces = [new CsTypeDeclaration(typeof(IViewModel))],
                ValueProperties =
                [
                    new MetadataValuePropertyDraft(
                        "Value",
                        new CsTypeDeclaration(typeof(int)),
                        new MetadataColumnDraft("value"))
                    {
                        CsSize = sizeof(int)
                    }
                ]
            },
            new MetadataTableDraft("materialization_view")
            {
                Type = TableType.View,
                Definition = ""
            });

    private static TableDefinition BuildTable(
        MetadataModelDraft model,
        MetadataTableDraft table)
    {
        var draft = new MetadataDatabaseDraft(
            "ModelMaterializationServicesDb",
            new CsTypeDeclaration(typeof(ModelMaterializationServicesTests)))
        {
            TableModels = [new MetadataTableModelDraft("Rows", model, table)]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException().TableModels.Single().Table;
    }

    private static MetadataScalarConverterDraft CreateConverterDraft(RecordingScalarConverter converter) =>
        new(
            new CsTypeDeclaration(typeof(MaterializedId)),
            new CsTypeDeclaration(typeof(int)),
            new CsTypeDeclaration(typeof(RecordingScalarConverter)),
            () => converter)
        {
            Origin = ScalarConverterOrigin.Property
        };

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new Exception($"Expected exception of type '{typeof(TException).Name}'.");
    }

    private sealed class RecordingRuntime : IModelMaterializationRuntime
    {
        private readonly Dictionary<DataLinqKey, IImmutableInstance> cache = [];

        public bool AcceptCacheAdds { get; init; } = true;
        public IImmutableInstance? CompetingWinner { get; init; }
        public Exception? FactoryException { get; init; }
        public bool ReturnDefaultPublication { get; init; }
        public int CacheLookupCalls { get; private set; }
        public int FactoryCalls { get; private set; }
        public int CacheAddCalls { get; private set; }
        public int CacheHitMetrics { get; private set; }
        public int CacheMissMetrics { get; private set; }
        public int MaterializationMetrics { get; private set; }
        public int CacheInsertionMetrics { get; private set; }
        public DataLinqKey? LastLookupKey { get; private set; }
        public DataLinqKey? LastCacheKey { get; private set; }
        public RowData? LastCacheRow { get; private set; }
        public TestImmutableInstance? LastCreatedInstance { get; private set; }

        public void Seed(DataLinqKey key, IImmutableInstance instance) => cache.Add(key, instance);

        public bool TryGetCached(
            TableDefinition table,
            DataLinqKey canonicalProviderKey,
            out IImmutableInstance? instance)
        {
            CacheLookupCalls++;
            LastLookupKey = canonicalProviderKey;
            return cache.TryGetValue(canonicalProviderKey, out instance);
        }

        public IImmutableInstance CreateImmutable(RowData rowData)
        {
            FactoryCalls++;
            if (FactoryException is not null)
                throw FactoryException;

            LastCreatedInstance = new TestImmutableInstance(rowData);
            return LastCreatedInstance;
        }

        public ModelCachePublicationResult PublishCached(
            TableDefinition table,
            DataLinqKey canonicalProviderKey,
            RowData rowData,
            IImmutableInstance instance)
        {
            CacheAddCalls++;
            LastCacheKey = canonicalProviderKey;
            LastCacheRow = rowData;

            if (ReturnDefaultPublication)
                return default;

            if (CompetingWinner is not null)
            {
                cache[canonicalProviderKey] = CompetingWinner;
                return ModelCachePublicationResult.Existing(CompetingWinner);
            }

            if (!AcceptCacheAdds)
                return ModelCachePublicationResult.NotCached();

            return cache.TryAdd(canonicalProviderKey, instance)
                ? ModelCachePublicationResult.Inserted()
                : ModelCachePublicationResult.Existing(cache[canonicalProviderKey]);
        }

        public void RecordCacheLookup(TableDefinition table, bool hit)
        {
            if (hit)
                CacheHitMetrics++;
            else
                CacheMissMetrics++;
        }

        public void RecordMaterialization(TableDefinition table) => MaterializationMetrics++;

        public void RecordCacheInsertion(TableDefinition table) => CacheInsertionMetrics++;
    }

    private sealed class RecordingScalarConverter(Func<object?, object?> fromProvider) : IDataLinqScalarConverter
    {
        public Type ModelType => typeof(MaterializedId);
        public Type ProviderType => typeof(int);
        public int FromProviderCalls { get; private set; }

        public object? ToProviderObject(object? modelValue, in ScalarConversionContext context) =>
            ((MaterializedId)modelValue!).Value;

        public object? FromProviderObject(object? providerValue, in ScalarConversionContext context)
        {
            FromProviderCalls++;
            return fromProvider(providerValue);
        }
    }

    private sealed record MaterializedId(int Value);

    private sealed class MaterializationRowModel;

    private sealed class TestImmutableInstance(IRowData? rowData = null) : IImmutableInstance
    {
        public object? this[string propertyName] => throw new NotSupportedException();
        public object? this[ColumnDefinition column] => throw new NotSupportedException();

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() => [];
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(IEnumerable<ColumnDefinition> columns) => [];
        public bool HasPrimaryKeysSet() => true;
        public ModelDefinition Metadata() => throw new NotSupportedException();
        public DataLinqKey PrimaryKeys() => DataLinqKey.Null;
        public IRowData GetRowData() => rowData ?? throw new NotSupportedException();
        IRowData IModelInstance.GetRowData() => GetRowData();
        public void ClearLazy() { }
        public V? GetLazy<V>(string name, Func<V> fetchCode) => fetchCode();
        public IDataSourceAccess GetDataSource() => throw new NotSupportedException();
    }
}
