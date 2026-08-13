using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public readonly record struct ScalarMetadataId(int Value);

public sealed class ScalarMetadataIdConverter : DataLinqScalarConverter<ScalarMetadataId, int>
{
    public int ToProviderCalls { get; private set; }
    public int FromProviderCalls { get; private set; }

    public override int ToProvider(ScalarMetadataId modelValue, in ScalarConversionContext context)
    {
        ToProviderCalls++;
        return modelValue.Value;
    }

    public override ScalarMetadataId FromProvider(int providerValue, in ScalarConversionContext context)
    {
        FromProviderCalls++;
        return new ScalarMetadataId(providerValue);
    }
}

public sealed class ScalarConverterMetadataTests
{
    [Test]
    public async Task ConvertedColumn_ExposesDistinctModelProviderAndConverterMetadata()
    {
        var factoryCalls = 0;
        var database = BuildDatabase(() =>
        {
            factoryCalls++;
            return new ScalarMetadataIdConverter();
        });
        var column = database.TableModels.Single().Table.Columns.Single();
        var component = database.TableModels.Single().Table.PrimaryKeyShape[0];

        await Assert.That(factoryCalls).IsEqualTo(1);
        await Assert.That(column.ModelClrType).IsEqualTo(typeof(ScalarMetadataId));
        await Assert.That(column.ProviderClrType).IsEqualTo(typeof(int));
        await Assert.That(column.ScalarMapping.ConverterClrType).IsEqualTo(typeof(ScalarMetadataIdConverter));
        await Assert.That(column.ScalarConverter).IsTypeOf<ScalarMetadataIdConverter>();
        await Assert.That(column.HasScalarConverter).IsTrue();
        await Assert.That(column.DbTypes.Single().Name).IsEqualTo("integer");

        await Assert.That(component.ModelClrType).IsEqualTo(typeof(ScalarMetadataId));
        await Assert.That(component.ProviderClrType).IsEqualTo(typeof(int));
        await Assert.That(component.HasScalarConverter).IsTrue();
        await Assert.That(component.ScalarConverterHandle).IsNotNull();
        await Assert.That(component.ProviderStoreKind).IsEqualTo(TableKeyComponentStoreKind.Unsupported);
        await Assert.That(database.TableModels.Single().Table.PrimaryKeyShape.SupportsScalarProviderKeyStore).IsFalse();
    }

    [Test]
    public async Task GenericConverter_ObjectBoundary_OwnsNullHandling()
    {
        var database = BuildDatabase(static () => new ScalarMetadataIdConverter());
        var column = database.TableModels.Single().Table.Columns.Single();
        var converter = (ScalarMetadataIdConverter)column.ScalarConverter!;
        var context = new ScalarConversionContext(column);

        var nullProvider = converter.ToProviderObject(null, context);
        var nullModel = converter.FromProviderObject(null, context);
        var provider = converter.ToProviderObject(new ScalarMetadataId(42), context);
        var model = converter.FromProviderObject(43, context);

        await Assert.That(nullProvider).IsNull();
        await Assert.That(nullModel).IsNull();
        await Assert.That(provider).IsEqualTo(42);
        await Assert.That(model).IsEqualTo(new ScalarMetadataId(43));
        await Assert.That(converter.ToProviderCalls).IsEqualTo(1);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(1);
        await Assert.That(converter.ModelType).IsEqualTo(typeof(ScalarMetadataId));
        await Assert.That(converter.ProviderType).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task AssemblyStyleMapping_ReusesOneConverterInstancePerMetadataBuild()
    {
        var factoryCalls = 0;
        Func<IDataLinqScalarConverter> factory = () =>
        {
            factoryCalls++;
            return new ScalarMetadataIdConverter();
        };

        var scalarDraft = CreateScalarDraft(factory, ScalarConverterOrigin.AssemblyRegistration);
        var draft = CreateDatabaseDraft(
            CreateValuePropertyDraft("Id", "id", scalarDraft, primaryKey: true),
            CreateValuePropertyDraft("ParentId", "parent_id", scalarDraft));
        var database = new MetadataDefinitionFactory().Build(draft).ValueOrException();
        var columns = database.TableModels.Single().Table.Columns;

        await Assert.That(factoryCalls).IsEqualTo(1);
        await Assert.That(ReferenceEquals(columns[0].ScalarConverter, columns[1].ScalarConverter)).IsTrue();
        await Assert.That(columns.All(column => column.ScalarMapping.Origin == ScalarConverterOrigin.AssemblyRegistration)).IsTrue();
    }

    [Test]
    public async Task PrimitiveIdentityColumn_KeepsExistingKeyMetadataBehavior()
    {
        var draft = CreateDatabaseDraft(
            new MetadataValuePropertyDraft(
                "Id",
                new CsTypeDeclaration(typeof(int)),
                new MetadataColumnDraft("id") { PrimaryKey = true }));
        var database = new MetadataDefinitionFactory().Build(draft).ValueOrException();
        var column = database.TableModels.Single().Table.Columns.Single();
        var component = database.TableModels.Single().Table.PrimaryKeyShape[0];

        await Assert.That(column.ModelClrType).IsEqualTo(typeof(int));
        await Assert.That(column.ProviderClrType).IsEqualTo(typeof(int));
        await Assert.That(column.HasScalarConverter).IsFalse();
        await Assert.That(component.ProviderStoreKind).IsEqualTo(TableKeyComponentStoreKind.Int32);
        await Assert.That(component.ScalarConverterHandle).IsNull();
    }

    [Test]
    public async Task GeneratedRuntimeMetadata_InstantiatesTheResolvedConverterWithoutKeyFastPaths()
    {
        var database = MetadataFromTypeFactory
            .ParseDatabaseFromDatabaseModel<ScalarGeneratedMetadataDb>()
            .ValueOrException();
        var tableModel = database.TableModels.Single();
        var column = tableModel.Table.Columns.Single();

        await Assert.That(column.ModelClrType).IsEqualTo(typeof(ScalarMetadataId));
        await Assert.That(column.ProviderClrType).IsEqualTo(typeof(int));
        await Assert.That(column.ScalarConverter).IsTypeOf<ScalarMetadataIdConverter>();
        await Assert.That(column.ScalarMapping.IsConverterResolved).IsTrue();
        await Assert.That(column.ScalarMapping.SourceLocation.HasValue).IsTrue();
        await Assert.That(column.ScalarMapping.SourceLocation!.Value.File.Name).IsEqualTo("ScalarConverterMetadataTests.cs");
        await Assert.That(column.ValueProperty.Attributes.OfType<ScalarConverterAttribute>().Single().ConverterType).IsEqualTo(typeof(ScalarMetadataIdConverter));
        await Assert.That(tableModel.Model.ProviderKeyRowStoreAccessor).IsNull();
        await Assert.That(tableModel.Table.PrimaryKeyShape.SupportsScalarProviderKeyStore).IsFalse();
    }

    [Test]
    public async Task TypedDraftConverterIdentityWithoutFactory_IsRejectedAsUnresolvedRuntimeMetadata()
    {
        var unresolved = new MetadataScalarConverterDraft(
            new CsTypeDeclaration(nameof(ScalarMetadataId), typeof(ScalarMetadataId).Namespace!, ModelCsType.Struct),
            new CsTypeDeclaration("Int32", "System", ModelCsType.Primitive),
            new CsTypeDeclaration(nameof(ScalarMetadataIdConverter), typeof(ScalarMetadataIdConverter).Namespace!, ModelCsType.Class));
        var draft = CreateDatabaseDraft(
            CreateValuePropertyDraft("Id", "id", unresolved, primaryKey: true));
        var result = new MetadataDefinitionFactory().Build(draft);

        await Assert.That(result.HasFailed).IsTrue();
        await Assert.That(result.Failure.Value.ToString()).Contains("does not provide a strongly typed factory");
    }

    private static DatabaseDefinition BuildDatabase(Func<IDataLinqScalarConverter> factory)
    {
        var draft = CreateDatabaseDraft(
            CreateValuePropertyDraft(
                "Id",
                "id",
                CreateScalarDraft(factory, ScalarConverterOrigin.Property),
                primaryKey: true));

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private static MetadataScalarConverterDraft CreateScalarDraft(
        Func<IDataLinqScalarConverter> factory,
        ScalarConverterOrigin origin) =>
        new(
            new CsTypeDeclaration(typeof(ScalarMetadataId)),
            new CsTypeDeclaration(typeof(int)),
            new CsTypeDeclaration(typeof(ScalarMetadataIdConverter)),
            factory)
        {
            Origin = origin
        };

    private static MetadataValuePropertyDraft CreateValuePropertyDraft(
        string propertyName,
        string columnName,
        MetadataScalarConverterDraft scalarConverter,
        bool primaryKey = false) =>
        new(
            propertyName,
            new CsTypeDeclaration(typeof(ScalarMetadataId)),
            new MetadataColumnDraft(columnName)
            {
                PrimaryKey = primaryKey,
                DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "integer")]
            })
        {
            ScalarConverter = scalarConverter
        };

    private static MetadataDatabaseDraft CreateDatabaseDraft(params MetadataValuePropertyDraft[] properties) =>
        new("ScalarMetadata", new CsTypeDeclaration(typeof(ScalarConverterMetadataTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(ScalarMetadataRow)))
                    {
                        ValueProperties = properties
                    },
                    new MetadataTableDraft("scalar_metadata"))
            ]
        };

    private sealed class ScalarMetadataRow;
}

[Database("scalar_generated_metadata")]
public partial class ScalarGeneratedMetadataDb(DataSourceAccess dataSource)
    : IDatabaseModel<ScalarGeneratedMetadataDb>
{
    public DbRead<ScalarGeneratedMetadataRow> Rows { get; } = new(dataSource);
}

[Table("scalar_generated_rows")]
public abstract partial class ScalarGeneratedMetadataRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<ScalarGeneratedMetadataRow, ScalarGeneratedMetadataDb>(rowData, dataSource),
      ITableModel<ScalarGeneratedMetadataDb>
{
    [PrimaryKey]
    [Column("id")]
    [ScalarConverter(typeof(ScalarMetadataIdConverter))]
    public abstract ScalarMetadataId Id { get; }
}
