using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class SqlMembershipTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EmptyAndNullMembershipHaveDefinedSqlSemantics(TestProviderDescriptor provider)
    {
        using var scope = TemporaryModelTestDatabase<SqlMembershipDb>.Create(provider, nameof(EmptyAndNullMembershipHaveDefinedSqlSemantics));
        var database = scope.Database;
        database.Insert(new MutableSqlMembershipRow { Id = 1, Value = null });
        database.Insert(new MutableSqlMembershipRow { Id = 2, Value = "a" });
        database.Insert(new MutableSqlMembershipRow { Id = 3, Value = "b" });
        (string?[] Values, int[] InIds, int[] NotInIds)[] cases =
        [
            ([], [], [1, 2, 3]),
            ([null], [], []),
            ([null, null], [], []),
            (["a", null], [2], []),
            (["a"], [2], [3]),
            (["a", "b"], [2, 3], []),
            (["missing"], [], [2, 3])
        ];

        foreach (var (values, inIds, notInIds) in cases)
        foreach (var negated in new[] { false, true })
        foreach (var notIn in new[] { false, true })
        {
            var query = new SqlQuery<SqlMembershipRow>(database.Provider.ReadOnlyAccess).OrderBy("id");
            var condition = negated ? query.WhereNot("value") : query.Where("value");
            if (notIn)
                condition.NotIn(values);
            else
                condition.In(values);
            var expected = notIn ^ negated ? notInIds : inIds;
            using var command = query.SelectQuery().ToDbCommand();
            await Assert.That(command.Parameters.Count).IsEqualTo(values.Length);
            if (values.Length == 0)
                await Assert.That(command.CommandText.Contains(notIn ^ negated ? "1=1" : "1=0", StringComparison.Ordinal)).IsTrue();
            else
                await Assert.That(command.CommandText.Contains(" IN (", StringComparison.Ordinal)).IsTrue();

            var actual = query.SelectQuery().ExecuteAs<SqlMembershipRow>().Select(row => row.Id).ToArray();
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        }

        var combined = new SqlQuery<SqlMembershipRow>(database.Provider.ReadOnlyAccess)
            .Where("value").In(Array.Empty<string>()).Or("id").EqualTo(2);
        await Assert.That(combined.Select().Single().Id).IsEqualTo(2);
        var combinedTrue = new SqlQuery<SqlMembershipRow>(database.Provider.ReadOnlyAccess)
            .Where("value").NotIn(Array.Empty<string>()).And("id").EqualTo(3);
        await Assert.That(combinedTrue.Select().Single().Id).IsEqualTo(3);
        var combinedFalse = new SqlQuery<SqlMembershipRow>(database.Provider.ReadOnlyAccess)
            .Where("value").In(Array.Empty<string>()).And("id").EqualTo(2);
        await Assert.That(combinedFalse.Select().Any()).IsFalse();
        var combinedAll = new SqlQuery<SqlMembershipRow>(database.Provider.ReadOnlyAccess)
            .Where("value").NotIn(Array.Empty<string>()).Or("id").EqualTo(2);
        await Assert.That(combinedAll.Select().Count()).IsEqualTo(3);

        IEnumerable<string> emptyEnumerable = Enumerable.Empty<string>();
        var enumerableQuery = new SqlQuery<SqlMembershipRow>(database.Provider.ReadOnlyAccess)
            .Where("value").In(emptyEnumerable);
        await Assert.That(enumerableQuery.Select().Any()).IsFalse();
        await Assert.That(() => new SqlQuery<SqlMembershipRow>(database.Provider.ReadOnlyAccess).Where("value").In((string[])null!))
            .Throws<ArgumentNullException>();
    }
}

[Database("sqlmembership")]
public sealed partial class SqlMembershipDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<SqlMembershipRow> Rows { get; } = new(dataSource);
}

[Table("sql_membership_rows")]
public abstract partial class SqlMembershipRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<SqlMembershipRow, SqlMembershipDb>(rowData, dataSource), ITableModel<SqlMembershipDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [Nullable, Column("value"), Type(DatabaseType.MySQL, "varchar", 40), Type(DatabaseType.MariaDB, "varchar", 40)]
    public abstract string? Value { get; }
}
