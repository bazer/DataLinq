using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Exceptions;
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

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitJoinLocalProjection_MaterializesConvertedJoinedSourceValuesAcrossProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<TypedIdRelationProjectionDb>.Create(
            provider,
            nameof(ExplicitJoinLocalProjection_MaterializesConvertedJoinedSourceValuesAcrossProviders));

        var insertedParents = databaseScope.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO typedidrelationparents (id, related_value, optional_value) VALUES " +
            "(1, 501, NULL), (2, 502, 602)");
        var insertedChildren = databaseScope.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO typedidrelationchildren (id, parent_id, name) VALUES " +
            "(10, 1, 'alpha'), (11, 1, 'beta'), (12, 2, 'gamma')");
        var database = databaseScope.Database;

        var query = database.Query().Children.Join(
            database.Query().Parents,
            child => child.ParentId,
            parent => parent.Id,
            (child, parent) => new object?[]
            {
                child.Id,
                child.Name,
                parent.RelatedValue,
                parent.OptionalValue,
                (QueryTypedId?)parent.RelatedValue,
                (object)parent.RelatedValue,
                (object?)parent.OptionalValue,
                parent.OptionalValue.HasValue,
                parent.OptionalValue.HasValue
                    ? (object)parent.OptionalValue.Value
                    : null
            });
        var invocation = ExpressionQueryPlanParser.Convert(database, query);
        var projection = invocation.Template.Projection as QueryPlanProjection.JoinedRowLocal;
        var recipe = projection?.Recipe as QueryPlanProjectionRecipe.NewArray;

        database.Provider.State.ClearCache();
        var rows = query
            .ToList()
            .OrderBy(static row => (int)row[0]!)
            .ToArray();
        var strictException = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanExecutor.ExecuteEnumerable<object?[]>(
                database.Provider.ReadOnlyAccess,
                invocation,
                ProjectionEvaluationOptions.AotStrict)
            .ToList());

        await Assert.That(insertedParents).IsEqualTo(2);
        await Assert.That(insertedChildren).IsEqualTo(3);
        await Assert.That(projection).IsNotNull();
        await Assert.That(projection!.Members).IsEmpty();
        await Assert.That(projection.Disposition)
            .IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);
        await Assert.That(recipe).IsNotNull();
        await Assert.That(recipe!.Disposition).IsEqualTo(QueryPlanProjectionDisposition.AotSafe);
        await Assert.That(recipe.Elements.Count).IsEqualTo(9);

        var rootSources = invocation.Template.Sources
            .Where(static source => source.Kind == QueryPlanSourceKind.RootTable)
            .ToArray();
        var explicitJoinSources = invocation.Template.Sources
            .Where(static source => source.Kind == QueryPlanSourceKind.ExplicitJoin)
            .ToArray();
        var joins = invocation.Template.Operations
            .OfType<QueryPlanOperation.Join>()
            .ToArray();
        await Assert.That(invocation.Template.Sources.Count).IsEqualTo(2);
        await Assert.That(rootSources.Length).IsEqualTo(1);
        await Assert.That(explicitJoinSources.Length).IsEqualTo(1);
        await Assert.That(explicitJoinSources[0].Table.DbName).IsEqualTo("typedidrelationparents");
        foreach (var source in new[] { rootSources[0], explicitJoinSources[0] })
        {
            await Assert.That(source.Table.PrimaryKeyColumns.Count).IsEqualTo(1);
            await Assert.That(source.Table.PrimaryKeyColumns[0].HasScalarConverter).IsFalse();
            await Assert.That(source.Table.PrimaryKeyColumns[0].ValueProperty.CsType.Type).IsEqualTo(typeof(int));
        }

        await Assert.That(joins.Length).IsEqualTo(1);
        await Assert.That(joins[0].JoinShape.Kind).IsEqualTo(QueryPlanJoinKind.Inner);
        await Assert.That(joins[0].JoinShape.LeftSource).IsEqualTo(rootSources[0]);
        await Assert.That(joins[0].JoinShape.RightSource).IsEqualTo(explicitJoinSources[0]);
        await Assert.That(joins[0].JoinShape.LeftColumn.DbName).IsEqualTo("parent_id");
        await Assert.That(joins[0].JoinShape.RightColumn.DbName).IsEqualTo("id");
        await Assert.That(joins[0].JoinShape.LeftColumn.HasScalarConverter).IsFalse();
        await Assert.That(joins[0].JoinShape.RightColumn.HasScalarConverter).IsFalse();
        await Assert.That(joins[0].JoinShape.LeftColumn.ValueProperty.CsType.Type).IsEqualTo(typeof(int));
        await Assert.That(joins[0].JoinShape.RightColumn.ValueProperty.CsType.Type).IsEqualTo(typeof(int));

        var relatedColumn = GetRecipeColumn(recipe.Elements[2]);
        var optionalColumn = GetRecipeColumn(recipe.Elements[3]);
        await Assert.That(relatedColumn.SourceSlot).IsEqualTo(explicitJoinSources[0]);
        await Assert.That(optionalColumn.SourceSlot).IsEqualTo(explicitJoinSources[0]);
        await Assert.That(relatedColumn.Column.HasScalarConverter).IsTrue();
        await Assert.That(optionalColumn.Column.HasScalarConverter).IsTrue();

        await Assert.That(rows.Length).IsEqualTo(3);
        await Assert.That(rows.Select(static row => (int)row[0]!).ToArray())
            .IsEquivalentTo(new[] { 10, 11, 12 });
        await Assert.That(rows.Select(static row => (string)row[1]!).ToArray())
            .IsEquivalentTo(new[] { "alpha", "beta", "gamma" });
        await Assert.That(rows.Select(static row => (QueryTypedId)row[2]!).ToArray())
            .IsEquivalentTo(new[] { new QueryTypedId(501), new QueryTypedId(501), new QueryTypedId(502) });
        await Assert.That(rows.Select(static row => row[3] is QueryTypedId id ? id : (QueryTypedId?)null).ToArray())
            .IsEquivalentTo(new QueryTypedId?[] { null, null, new(602) });
        await Assert.That(rows.Select(static row => (QueryTypedId)row[4]!).ToArray())
            .IsEquivalentTo(new[] { new QueryTypedId(501), new QueryTypedId(501), new QueryTypedId(502) });
        await Assert.That(rows.All(static row => row[5] is QueryTypedId)).IsTrue();
        await Assert.That(rows[0][6]).IsNull();
        await Assert.That(rows[1][6]).IsNull();
        await Assert.That(rows[2][6]).IsTypeOf<QueryTypedId>();
        await Assert.That(rows.Select(static row => (bool)row[7]!).ToArray())
            .IsEquivalentTo(new[] { false, false, true });
        await Assert.That(rows[0][8]).IsNull();
        await Assert.That(rows[1][8]).IsNull();
        await Assert.That(rows[2][8]).IsEqualTo(new QueryTypedId(602));
        await Assert.That(rows.SelectMany(static row => row.Skip(2).Take(5)).Any(static value => value is int))
            .IsFalse();
        await Assert.That(rows.Any(static row => row[8] is int)).IsFalse();

        await Assert.That(strictException.Message).Contains("JoinedRowLocal");
        await Assert.That(strictException.Message).Contains("SQL-only compatibility");
    }

    private static QueryPlanColumnValue GetColumn(QueryPlanValue value)
    {
        while (value is QueryPlanConvertedValue converted)
            value = converted.Value;

        return value as QueryPlanColumnValue
            ?? throw new InvalidOperationException($"Expected a direct SQL column, but found '{value.Kind}'.");
    }

    private static QueryPlanProjectionRecipe.SourceColumn GetRecipeColumn(QueryPlanProjectionRecipe recipe)
    {
        while (recipe is QueryPlanProjectionRecipe.Convert converted)
            recipe = converted.Operand;

        return recipe as QueryPlanProjectionRecipe.SourceColumn
            ?? throw new InvalidOperationException($"Expected a source-column recipe, but found '{recipe.Kind}'.");
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
