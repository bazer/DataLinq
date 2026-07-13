using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class TypedIdImplicitRelationProjectionTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ImplicitRelationSqlRowProjection_MaterializesConvertedRelatedValuesAcrossProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<TypedIdRelationProjectionDb>.Create(
            provider,
            nameof(ImplicitRelationSqlRowProjection_MaterializesConvertedRelatedValuesAcrossProviders));

        var insertedParents = databaseScope.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO typedidrelationparents (id, related_value, optional_value) VALUES " +
            "(1, 501, NULL), (2, 502, 602)");
        var insertedChildren = databaseScope.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO typedidrelationchildren (id, parent_id, name) VALUES " +
            "(10, 1, 'alpha'), (11, 1, 'beta'), (12, 2, 'gamma')");
        var database = databaseScope.Database;

        var query = database.Query().Children
            .OrderBy(child => child.Id)
            .Select(child => new
            {
                child.Id,
                child.Name,
                Related = child.Parent.RelatedValue,
                Optional = child.Parent.OptionalValue,
                Lifted = (QueryTypedId?)child.Parent.RelatedValue,
                Boxed = (object)child.Parent.RelatedValue,
                BoxedOptional = (object?)child.Parent.OptionalValue
            });
        var invocation = ExpressionQueryPlanParser.Convert(database, query);
        var projection = invocation.Template.Projection as QueryPlanProjection.SqlRow;
        var rows = query.ToList();
        var sql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(database, query);
        var normalizedSql = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sql.Text);

        var betaName = "beta";
        var singleRow = database.Query().Children
            .Where(child => child.Name == betaName)
            .Select(child => new
            {
                child.Name,
                Related = child.Parent.RelatedValue,
                Optional = child.Parent.OptionalValue
            })
            .Single();

        await Assert.That(insertedParents).IsEqualTo(2);
        await Assert.That(insertedChildren).IsEqualTo(3);
        await Assert.That(projection).IsNotNull();
        await Assert.That(projection!.Disposition)
            .IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);

        var implicitSources = invocation.Template.Sources
            .Where(static source => source.Kind == QueryPlanSourceKind.ImplicitJoin)
            .ToArray();
        await Assert.That(invocation.Template.Sources.Count).IsEqualTo(2);
        await Assert.That(implicitSources.Length).IsEqualTo(1);
        await Assert.That(implicitSources[0].Table.DbName).IsEqualTo("typedidrelationparents");

        var relatedColumns = projection.Members
            .Select(static member => GetColumn(member.Value))
            .Where(static column => column.Source.Kind == QueryPlanSourceKind.ImplicitJoin)
            .ToArray();
        await Assert.That(relatedColumns.Length).IsEqualTo(5);
        await Assert.That(relatedColumns.All(static column => column.Column.HasScalarConverter)).IsTrue();

        var constructorParameterTypes = projection.Constructor.GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ToArray();
        await Assert.That(constructorParameterTypes.SequenceEqual(
            new[]
            {
                typeof(int),
                typeof(string),
                typeof(QueryTypedId),
                typeof(QueryTypedId?),
                typeof(QueryTypedId?),
                typeof(object),
                typeof(object)
            })).IsTrue();

        await Assert.That(normalizedSql).Contains("JOIN");
        await Assert.That(normalizedSql).Contains("related_value");
        await Assert.That(rows.Select(static row => row.Id).ToArray()).IsEquivalentTo(new[] { 10, 11, 12 });
        await Assert.That(rows.Select(static row => row.Related).ToArray())
            .IsEquivalentTo(new[] { new QueryTypedId(501), new QueryTypedId(501), new QueryTypedId(502) });
        await Assert.That(rows.Select(static row => row.Optional).ToArray())
            .IsEquivalentTo(new QueryTypedId?[] { null, null, new(602) });
        await Assert.That(rows.Select(static row => row.Lifted).ToArray())
            .IsEquivalentTo(new QueryTypedId?[] { new(501), new(501), new(502) });
        await Assert.That(rows.All(static row => row.Boxed is QueryTypedId)).IsTrue();
        await Assert.That(rows[0].BoxedOptional).IsNull();
        await Assert.That(rows[1].BoxedOptional).IsNull();
        await Assert.That(rows[2].BoxedOptional).IsTypeOf<QueryTypedId>();

        await Assert.That(singleRow.Name).IsEqualTo("beta");
        await Assert.That(singleRow.Related).IsEqualTo(new QueryTypedId(501));
        await Assert.That(singleRow.Optional).IsNull();
    }

    private static QueryPlanColumnValue GetColumn(QueryPlanValue value)
    {
        while (value is QueryPlanConvertedValue converted)
            value = converted.Value;

        return value as QueryPlanColumnValue
            ?? throw new InvalidOperationException($"Expected a direct SQL column, but found '{value.Kind}'.");
    }
}

[Database("typedidrelationprojection")]
public sealed partial class TypedIdRelationProjectionDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<TypedIdRelationParent> Parents { get; } = new(dataSource);
    public DbRead<TypedIdRelationChild> Children { get; } = new(dataSource);
}

[Table("typedidrelationparents")]
public abstract partial class TypedIdRelationParent(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<TypedIdRelationParent, TypedIdRelationProjectionDb>(rowData, dataSource),
      ITableModel<TypedIdRelationProjectionDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("id")]
    public abstract int Id { get; }

    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(QueryTypedIdConverter))]
    [Column("related_value")]
    public abstract QueryTypedId RelatedValue { get; }

    [Nullable]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [ScalarConverter(typeof(QueryTypedIdConverter))]
    [Column("optional_value")]
    public abstract QueryTypedId? OptionalValue { get; }

    [Relation("typedidrelationchildren", "parent_id", "FK_typedidrelation_child_parent")]
    public abstract IImmutableRelation<TypedIdRelationChild> Children { get; }
}

[Table("typedidrelationchildren")]
public abstract partial class TypedIdRelationChild(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<TypedIdRelationChild, TypedIdRelationProjectionDb>(rowData, dataSource),
      ITableModel<TypedIdRelationProjectionDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("id")]
    public abstract int Id { get; }

    [ForeignKey("typedidrelationparents", "id", "FK_typedidrelation_child_parent")]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("parent_id")]
    public abstract int ParentId { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 40)]
    [Type(DatabaseType.MariaDB, "varchar", 40)]
    [Column("name")]
    public abstract string Name { get; }

    [Relation("typedidrelationparents", "id", "FK_typedidrelation_child_parent")]
    public abstract TypedIdRelationParent Parent { get; }
}
