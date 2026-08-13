using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.MariaDB;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.SQLite;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class EffectiveColumnTypeResolverTests
{
    [Test]
    public async Task TypedGuidId_UsesCanonicalProviderTypeForProviderDefaults()
    {
        var column = CreateColumn(
            new CsTypeDeclaration(typeof(TypedGuidId)),
            scalarConverter: new MetadataScalarConverterDraft(
                new CsTypeDeclaration(typeof(TypedGuidId)),
                new CsTypeDeclaration(typeof(Guid)),
                new CsTypeDeclaration(typeof(TypedGuidIdConverter)),
                static () => new TypedGuidIdConverter()));

        await AssertResolutionAndFactoryParity(
            column,
            DatabaseType.MySQL,
            "binary",
            16);
        await AssertResolutionAndFactoryParity(
            column,
            DatabaseType.MariaDB,
            "uuid",
            null);
        await AssertResolutionAndFactoryParity(
            column,
            DatabaseType.SQLite,
            "TEXT",
            null);
    }

    [Test]
    public async Task DefaultUuid_UsesProviderSpecificPhysicalTypesCaseInsensitively()
    {
        var column = CreateColumn(
            new CsTypeDeclaration(typeof(Guid)),
            [new DatabaseColumnType(DatabaseType.Default, "uUiD")]);

        await AssertResolutionAndFactoryParity(
            column,
            DatabaseType.MySQL,
            "binary",
            16);
        await AssertResolutionAndFactoryParity(
            column,
            DatabaseType.MariaDB,
            "uuid",
            null);
        await AssertResolutionAndFactoryParity(
            column,
            DatabaseType.SQLite,
            "TEXT",
            null);
    }

    [Test]
    public async Task DefaultUuidModifiers_OnGuidColumn_AreRejectedInsteadOfNormalized()
    {
        var result = new MetadataDefinitionFactory().Build(CreateDraft(
            new CsTypeDeclaration(typeof(Guid)),
            [
                new DatabaseColumnType(
                    DatabaseType.Default,
                    "uuid",
                    length: 36,
                    decimals: 2,
                    signed: false)
            ]));

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.Message).Contains("unsupported effective MySQL");
        await Assert.That(failure.Message).Contains("binary(36)");
    }

    [Test]
    public async Task DefaultUuidModifiers_OnNonGuidColumn_PreserveLegacyTranslation()
    {
        var column = CreateColumn(
            new CsTypeDeclaration(typeof(string)),
            [
                new DatabaseColumnType(
                    DatabaseType.Default,
                    "uuid",
                    length: 36,
                    decimals: 2,
                    signed: false)
            ]);
        var mySqlType = new SqlFromMySqlFactory().GetDbType(column);
        var mariaDbType = new SqlFromMariaDBFactory().GetDbType(column);

        await Assert.That(mySqlType.Name).IsEqualTo("binary");
        await Assert.That(mySqlType.Length).IsEqualTo(36UL);
        await Assert.That(mySqlType.Decimals).IsEqualTo(2U);
        await Assert.That(mySqlType.Signed).IsFalse();
        await Assert.That(mariaDbType.Name).IsEqualTo("binary");
        await Assert.That(mariaDbType.Length).IsEqualTo(36UL);
        await Assert.That(mariaDbType.Decimals).IsEqualTo(2U);
        await Assert.That(mariaDbType.Signed).IsFalse();
    }

    [Test]
    public async Task UnknownDefaultGuidType_DoesNotFabricateCanonicalFallback()
    {
        var result = new MetadataDefinitionFactory().Build(CreateDraft(
            new CsTypeDeclaration(typeof(Guid)),
            [new DatabaseColumnType(DatabaseType.Default, "custom_uuid")]));

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.Message).Contains("no effective database type");
        await Assert.That(failure.Message).Contains("MySQL");
    }

    [Test]
    public async Task CrossProviderTranslations_MatchFactoryResultsAndPreserveExactTypes()
    {
        var mariaDbColumn = CreateColumn(
            new CsTypeDeclaration(typeof(Guid)),
            [new DatabaseColumnType(DatabaseType.MariaDB, "UuId")]);

        await AssertResolutionAndFactoryParity(
            mariaDbColumn,
            DatabaseType.MySQL,
            "binary",
            16);
        await AssertResolutionAndFactoryParity(
            mariaDbColumn,
            DatabaseType.SQLite,
            "TEXT",
            null);

        var exactMySqlType = new DatabaseColumnType(DatabaseType.MySQL, "BiNaRy", 16);
        var mySqlColumn = CreateColumn(
            new CsTypeDeclaration(typeof(Guid)),
            [exactMySqlType]);
        var resolved = EffectiveColumnTypeResolver.Resolve(mySqlColumn, DatabaseType.MySQL);

        await Assert.That(ReferenceEquals(resolved, mySqlColumn.DbTypes.Single())).IsTrue();
        await Assert.That(resolved!.Name).IsEqualTo("BiNaRy");
        await Assert.That(new SqlFromMySqlFactory().GetDbType(mySqlColumn).Name)
            .IsEqualTo("BiNaRy");
    }

    [Test]
    public async Task ProviderFactories_PreserveExistingVirtualTranslationHooks()
    {
        var column = CreateColumn(
            new CsTypeDeclaration(typeof(int)),
            [new DatabaseColumnType(DatabaseType.Default, "custom")]);

        await Assert.That(new CustomMySqlFactory().GetDbType(column).Name)
            .IsEqualTo("custom_mysql");
        await Assert.That(new CustomMariaDbFactory().GetDbType(column).Name)
            .IsEqualTo("custom_mariadb");
        await Assert.That(new CustomSQLiteFactory().GetDbType(column).Name)
            .IsEqualTo("CUSTOM_SQLITE");
    }

    [Test]
    public async Task SQLiteFactory_ConverterFallback_PreservesExistingVirtualHook()
    {
        var column = CreateColumn(
            new CsTypeDeclaration(typeof(TypedGuidId)),
            scalarConverter: new MetadataScalarConverterDraft(
                new CsTypeDeclaration(typeof(TypedGuidId)),
                new CsTypeDeclaration(typeof(Guid)),
                new CsTypeDeclaration(typeof(TypedGuidIdConverter)),
                static () => new TypedGuidIdConverter()));

        await Assert.That(new ConverterAwareSQLiteFactory().GetDbType(column).Name)
            .IsEqualTo("CUSTOM_CONVERTER");
    }

    private static async Task AssertResolutionAndFactoryParity(
        ColumnDefinition column,
        DatabaseType databaseType,
        string expectedName,
        ulong? expectedLength)
    {
        var resolved = EffectiveColumnTypeResolver.Resolve(column, databaseType);
        var factoryType = databaseType switch
        {
            DatabaseType.MySQL => new SqlFromMySqlFactory().GetDbType(column),
            DatabaseType.MariaDB => new SqlFromMariaDBFactory().GetDbType(column),
            DatabaseType.SQLite => new SqlFromSQLiteFactory().GetDbType(column),
            _ => throw new ArgumentOutOfRangeException(nameof(databaseType), databaseType, null)
        };

        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved!.DatabaseType).IsEqualTo(databaseType);
        await Assert.That(resolved.Name).IsEqualTo(expectedName);
        await Assert.That(resolved.Length).IsEqualTo(expectedLength);
        await Assert.That(factoryType.DatabaseType).IsEqualTo(resolved.DatabaseType);
        await Assert.That(factoryType.Name).IsEqualTo(resolved.Name);
        await Assert.That(factoryType.Length).IsEqualTo(resolved.Length);
        await Assert.That(factoryType.Decimals).IsEqualTo(resolved.Decimals);
        await Assert.That(factoryType.Signed).IsEqualTo(resolved.Signed);
    }

    private static ColumnDefinition CreateColumn(
        CsTypeDeclaration modelType,
        IReadOnlyList<DatabaseColumnType>? dbTypes = null,
        MetadataScalarConverterDraft? scalarConverter = null)
    {
        return new MetadataDefinitionFactory()
            .Build(CreateDraft(modelType, dbTypes, scalarConverter))
            .ValueOrException()
            .TableModels
            .Single()
            .Table
            .Columns
            .Single();
    }

    private static MetadataDatabaseDraft CreateDraft(
        CsTypeDeclaration modelType,
        IReadOnlyList<DatabaseColumnType>? dbTypes = null,
        MetadataScalarConverterDraft? scalarConverter = null)
    {
        return new MetadataDatabaseDraft(
            "EffectiveTypeDb",
            new CsTypeDeclaration("EffectiveTypeDb", "DataLinq.Tests.Unit.Core", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(
                        new CsTypeDeclaration("EffectiveTypeRow", "DataLinq.Tests.Unit.Core", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                modelType,
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    DbTypes = dbTypes ?? []
                                })
                            {
                                Attributes =
                                [
                                    new PrimaryKeyAttribute(),
                                    new ColumnAttribute("id")
                                ],
                                ScalarConverter = scalarConverter
                            }
                        ]
                    },
                    new MetadataTableDraft("effective_type_rows"))
            ]
        };
    }

    private readonly record struct TypedGuidId(Guid Value);

    private sealed class TypedGuidIdConverter : DataLinqScalarConverter<TypedGuidId, Guid>
    {
        public override Guid ToProvider(
            TypedGuidId modelValue,
            in ScalarConversionContext context) => modelValue.Value;

        public override TypedGuidId FromProvider(
            Guid providerValue,
            in ScalarConversionContext context) => new(providerValue);
    }

    private sealed class CustomMySqlFactory : SqlFromMySqlFactory
    {
        protected override DatabaseColumnType? TryGetColumnType(DatabaseColumnType dbType) =>
            dbType.Name == "custom"
                ? new DatabaseColumnType(DatabaseType.MySQL, "custom_mysql")
                : base.TryGetColumnType(dbType);
    }

    private sealed class CustomMariaDbFactory : SqlFromMariaDBFactory
    {
        protected override DatabaseColumnType? TryGetColumnType(DatabaseColumnType dbType) =>
            dbType.Name == "custom"
                ? new DatabaseColumnType(DatabaseType.MariaDB, "custom_mariadb")
                : base.TryGetColumnType(dbType);
    }

    private sealed class CustomSQLiteFactory : SqlFromSQLiteFactory
    {
        protected override DatabaseColumnType? TryGetColumnType(DatabaseColumnType dbType) =>
            dbType.Name == "custom"
                ? new DatabaseColumnType(DatabaseType.SQLite, "CUSTOM_SQLITE")
                : base.TryGetColumnType(dbType);
    }

    private sealed class ConverterAwareSQLiteFactory : SqlFromSQLiteFactory
    {
        protected override DatabaseColumnType? GetDbTypeFromCsType(
            ValueProperty property,
            DatabaseType databaseType) =>
            property.Column.HasScalarConverter
                ? new DatabaseColumnType(DatabaseType.SQLite, "CUSTOM_CONVERTER")
                : base.GetDbTypeFromCsType(property, databaseType);
    }
}
