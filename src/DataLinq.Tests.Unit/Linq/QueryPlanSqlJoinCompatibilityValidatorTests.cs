using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Exceptions;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Sql;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Linq;

public sealed class QueryPlanSqlJoinCompatibilityValidatorTests
{
    [Test]
    public void ConverterFreeNonGuidJoin_PreservesTheExistingSqlCompatibilitySurface()
    {
        var left = CreateColumn("primitive_left", typeof(int));
        var right = CreateColumn("primitive_right", typeof(long));

        QueryPlanSqlJoinCompatibilityValidator.Validate(
            CreateTemplate(left, right, QueryPlanSourceKind.ExplicitJoin),
            DatabaseType.MySQL);
    }

    [Test]
    public void SameConverterMapping_AllowsNullableAndNonNullableKeysForExplicitAndImplicitJoins()
    {
        var left = CreateColumn("converted_left", typeof(JoinId?), new JoinIdConverter());
        var right = CreateColumn("converted_right", typeof(JoinId), new JoinIdConverter());

        QueryPlanSqlJoinCompatibilityValidator.Validate(
            CreateTemplate(left, right, QueryPlanSourceKind.ExplicitJoin),
            DatabaseType.SQLite);
        QueryPlanSqlJoinCompatibilityValidator.Validate(
            CreateTemplate(left, right, QueryPlanSourceKind.ImplicitJoin),
            DatabaseType.SQLite);
    }

    [Test]
    public async Task OneSidedConverterMapping_IsRejectedWithBothColumnMappings()
    {
        var left = CreateColumn("converted_left", typeof(JoinId), new JoinIdConverter());
        var right = CreateColumn("identity_right", typeof(int));

        var exception = Capture<QueryTranslationException>(() =>
            QueryPlanSqlJoinCompatibilityValidator.Validate(
                CreateTemplate(left, right, QueryPlanSourceKind.ExplicitJoin),
                DatabaseType.MySQL));

        await Assert.That(exception.Message).Contains("one key is scalar-converter-backed");
        await Assert.That(exception.Message).Contains("converted_left.id");
        await Assert.That(exception.Message).Contains("identity_right.id");
        await Assert.That(exception.Message).Contains("<identity>");
    }

    [Test]
    public async Task DifferentConverterTypes_AreRejectedNominallyWithoutInvokingEitherConverter()
    {
        var leftConverter = new JoinIdConverter();
        var rightConverter = new AlternateJoinIdConverter();
        var left = CreateColumn("nominal_left", typeof(JoinId), leftConverter);
        var right = CreateColumn("nominal_right", typeof(JoinId), rightConverter);

        var exception = Capture<QueryTranslationException>(() =>
            QueryPlanSqlJoinCompatibilityValidator.Validate(
                CreateTemplate(left, right, QueryPlanSourceKind.ExplicitJoin),
                DatabaseType.MariaDB));

        await Assert.That(exception.Message).Contains("scalar converter CLR types differ");
        await Assert.That(exception.Message).Contains(nameof(JoinIdConverter));
        await Assert.That(exception.Message).Contains(nameof(AlternateJoinIdConverter));
        await Assert.That(exception.Message).Contains("ExplicitJoin");
        await Assert.That(exception.Message).Contains(nameof(DatabaseType.MariaDB));
        await Assert.That(leftConverter.Calls).IsEqualTo(0);
        await Assert.That(rightConverter.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task DifferentCanonicalProviderTypes_AreRejectedBeforeConverterIdentity()
    {
        var left = CreateColumn("int_left", typeof(JoinId), new JoinIdConverter());
        var right = CreateColumn("long_right", typeof(JoinId), new LongJoinIdConverter());

        var exception = Capture<QueryTranslationException>(() =>
            QueryPlanSqlJoinCompatibilityValidator.Validate(
                CreateTemplate(left, right, QueryPlanSourceKind.ExplicitJoin),
                DatabaseType.SQLite));

        await Assert.That(exception.Message).Contains("canonical provider CLR types differ");
        await Assert.That(exception.Message).Contains(typeof(int).FullName!);
        await Assert.That(exception.Message).Contains(typeof(long).FullName!);
    }

    [Test]
    public async Task SameGuidFormat_AllowsDifferentDeclarationProvenance()
    {
        var explicitColumn = CreateGuidColumn(
            "guid_explicit",
            DatabaseType.MySQL,
            "binary",
            16,
            GuidStorageFormat.Binary16LittleEndian);
        var inferredColumn = CreateGuidColumn(
            "guid_inferred",
            DatabaseType.MySQL,
            "binary",
            16);
        var explicitStorage = explicitColumn.GetGuidStorageFor(DatabaseType.MySQL)!;
        var inferredStorage = inferredColumn.GetGuidStorageFor(DatabaseType.MySQL)!;

        QueryPlanSqlJoinCompatibilityValidator.Validate(
            CreateTemplate(explicitColumn, inferredColumn, QueryPlanSourceKind.ExplicitJoin),
            DatabaseType.MySQL);

        await Assert.That(explicitStorage.Format).IsEqualTo(inferredStorage.Format);
        await Assert.That(explicitStorage.IsExplicit).IsTrue();
        await Assert.That(inferredStorage.IsExplicit).IsFalse();
    }

    [Test]
    public async Task DifferentGuidFormats_AreRejectedForTheActiveProvider()
    {
        var rfcColumn = CreateGuidColumn(
            "guid_rfc",
            DatabaseType.MySQL,
            "binary",
            16,
            GuidStorageFormat.Binary16Rfc4122);
        var legacyColumn = CreateGuidColumn(
            "guid_legacy",
            DatabaseType.MySQL,
            "binary",
            16);

        var exception = Capture<QueryTranslationException>(() =>
            QueryPlanSqlJoinCompatibilityValidator.Validate(
                CreateTemplate(rfcColumn, legacyColumn, QueryPlanSourceKind.ExplicitJoin),
                DatabaseType.MySQL));

        await Assert.That(exception.Message).Contains("UUID storage formats differ");
        await Assert.That(exception.Message).Contains(nameof(GuidStorageFormat.Binary16Rfc4122));
        await Assert.That(exception.Message).Contains(nameof(GuidStorageFormat.Binary16LittleEndian));
    }

    [Test]
    public async Task UnresolvedGuidFormat_IsRejectedForTheActiveProvider()
    {
        var resolvedColumn = CreateGuidColumn(
            "guid_resolved",
            DatabaseType.MySQL,
            "binary",
            16);
        var unresolvedColumn = CreateGuidColumn(
            "guid_unresolved",
            DatabaseType.MySQL,
            "binary",
            16,
            buildProviderMetadata: true);

        var exception = Capture<QueryTranslationException>(() =>
            QueryPlanSqlJoinCompatibilityValidator.Validate(
                CreateTemplate(resolvedColumn, unresolvedColumn, QueryPlanSourceKind.ExplicitJoin),
                DatabaseType.MySQL));

        await Assert.That(exception.Message).Contains("UUID storage format is unresolved");
        await Assert.That(exception.Message).Contains("uuid=<unresolved>");
    }

    [Test]
    public async Task MissingGuidFormat_IsRejectedForTheActiveProvider()
    {
        var mysqlColumn = CreateGuidColumn(
            "guid_mysql",
            DatabaseType.MySQL,
            "binary",
            16);
        var sqliteOnlyColumn = CreateGuidColumn(
            "guid_sqlite_only",
            DatabaseType.SQLite,
            "text");

        var exception = Capture<QueryTranslationException>(() =>
            QueryPlanSqlJoinCompatibilityValidator.Validate(
                CreateTemplate(mysqlColumn, sqliteOnlyColumn, QueryPlanSourceKind.ExplicitJoin),
                DatabaseType.MySQL));

        await Assert.That(exception.Message).Contains("UUID storage format is missing");
        await Assert.That(exception.Message).Contains("uuid=<missing>");
    }

    [Test]
    public async Task IncompatibleImplicitJoinNestedInPushdown_IsStillRejected()
    {
        var left = CreateColumn("nested_left", typeof(JoinId), new JoinIdConverter());
        var right = CreateColumn("nested_right", typeof(JoinId), new AlternateJoinIdConverter());

        var exception = Capture<QueryTranslationException>(() =>
            QueryPlanSqlJoinCompatibilityValidator.Validate(
                CreateTemplate(
                    left,
                    right,
                    QueryPlanSourceKind.ImplicitJoin,
                    pushdown: true),
                DatabaseType.SQLite));

        await Assert.That(exception.Message).Contains("ImplicitJoin");
        await Assert.That(exception.Message).Contains("scalar converter CLR types differ");
    }

    private static QueryPlanTemplate CreateTemplate(
        ColumnDefinition leftColumn,
        ColumnDefinition rightColumn,
        QueryPlanSourceKind rightSourceKind,
        bool pushdown = false)
    {
        var leftSource = Source("s0", "t0", leftColumn.Table, QueryPlanSourceKind.RootTable);
        var rightSource = Source("s1", "t1", rightColumn.Table, rightSourceKind);
        var join = new QueryPlanOperation.Join(new QueryPlanJoin(
            QueryPlanJoinKind.Inner,
            leftSource,
            leftColumn,
            rightSource,
            rightColumn));
        QueryPlanOperation operation = pushdown
            ? new QueryPlanOperation.Pushdown([join], [])
            : join;

        return new QueryPlanTemplate(
            [leftSource, rightSource],
            [operation],
            new QueryPlanProjection.Entity(leftSource),
            QueryPlanResult.Sequence(leftSource.ElementType),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty);
    }

    private static QueryPlanSourceSlot Source(
        string id,
        string alias,
        TableDefinition table,
        QueryPlanSourceKind kind) =>
        new(
            id,
            alias,
            table,
            table.Model.CsType.Type!,
            kind,
            QueryPlanSourceCardinality.Many,
            IsNullable: false);

    private static ColumnDefinition CreateGuidColumn(
        string tableName,
        DatabaseType databaseType,
        string databaseColumnType,
        ulong? length = null,
        GuidStorageFormat? explicitFormat = null,
        bool buildProviderMetadata = false)
    {
        IReadOnlyList<Attribute> attributes = explicitFormat.HasValue
            ? [new GuidStorageAttribute(databaseType, explicitFormat.Value)]
            : [];

        return CreateColumn(
            tableName,
            typeof(Guid),
            dbTypes: [new DatabaseColumnType(databaseType, databaseColumnType, length)],
            extraAttributes: attributes,
            buildProviderMetadata: buildProviderMetadata);
    }

    private static ColumnDefinition CreateColumn(
        string tableName,
        Type modelType,
        IDataLinqScalarConverter? converter = null,
        IReadOnlyList<DatabaseColumnType>? dbTypes = null,
        IReadOnlyList<Attribute>? extraAttributes = null,
        bool buildProviderMetadata = false)
    {
        var attributes = new List<Attribute>
        {
            new PrimaryKeyAttribute(),
            new ColumnAttribute("id")
        };
        attributes.AddRange(extraAttributes ?? []);

        var property = new MetadataValuePropertyDraft(
            "Id",
            new CsTypeDeclaration(modelType),
            new MetadataColumnDraft("id")
            {
                PrimaryKey = true,
                DbTypes = dbTypes ?? []
            })
        {
            Attributes = attributes,
            ScalarConverter = converter is null
                ? null
                : new MetadataScalarConverterDraft(
                    new CsTypeDeclaration(converter.ModelType),
                    new CsTypeDeclaration(converter.ProviderType),
                    new CsTypeDeclaration(converter.GetType()),
                    () => converter)
                {
                    Origin = ScalarConverterOrigin.Property
                }
        };
        var draft = new MetadataDatabaseDraft(
            $"JoinCompatibility_{tableName}",
            new CsTypeDeclaration(typeof(QueryPlanSqlJoinCompatibilityValidatorTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(JoinCompatibilityRow)))
                    {
                        ValueProperties = [property]
                    },
                    new MetadataTableDraft(tableName))
            ]
        };
        var factory = new MetadataDefinitionFactory();
        var database = buildProviderMetadata
            ? factory.BuildProviderMetadata(draft).ValueOrException()
            : factory.Build(draft).ValueOrException();

        return database.TableModels.Single().Table.Columns.Single();
    }

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

    private readonly record struct JoinId(int Value);

    private sealed class JoinIdConverter : DataLinqScalarConverter<JoinId, int>
    {
        public int Calls { get; private set; }

        public override int ToProvider(JoinId modelValue, in ScalarConversionContext context)
        {
            Calls++;
            return modelValue.Value;
        }

        public override JoinId FromProvider(int providerValue, in ScalarConversionContext context)
        {
            Calls++;
            return new JoinId(providerValue);
        }
    }

    private sealed class AlternateJoinIdConverter : DataLinqScalarConverter<JoinId, int>
    {
        public int Calls { get; private set; }

        public override int ToProvider(JoinId modelValue, in ScalarConversionContext context)
        {
            Calls++;
            return checked(modelValue.Value + 1);
        }

        public override JoinId FromProvider(int providerValue, in ScalarConversionContext context)
        {
            Calls++;
            return new JoinId(checked(providerValue - 1));
        }
    }

    private sealed class LongJoinIdConverter : DataLinqScalarConverter<JoinId, long>
    {
        public override long ToProvider(JoinId modelValue, in ScalarConversionContext context) =>
            modelValue.Value;

        public override JoinId FromProvider(long providerValue, in ScalarConversionContext context) =>
            new(checked((int)providerValue));
    }

    private sealed class JoinCompatibilityRow;
}
