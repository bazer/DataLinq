using System;
using System.Linq;
using System.Text;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Linq;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Linq.Planning.Sql;
using DataLinq.Testing;

namespace DataLinq.Benchmark;

internal enum TypedIdExactTerminalScenario
{
    Cold,
    Warm
}

internal sealed class TypedIdExactTerminalBenchmarkContext : IDisposable
{
    internal const int OperationsPerInvoke = BenchmarkContext.BatchOperationCount;
    internal const string ColdDisplayName = "Cold typed-ID exact terminal";
    internal const string WarmDisplayName = "Warm typed-ID exact terminal";

    private readonly TemporaryModelTestDatabase<TypedIdExactTerminalBenchmarkDatabase> databaseScope;
    private readonly ExactTerminalBenchmarkId[] ids;

    internal TypedIdExactTerminalBenchmarkContext(TestProviderDescriptor provider)
    {
        databaseScope = TemporaryModelTestDatabase<TypedIdExactTerminalBenchmarkDatabase>.Create(
            provider,
            "typed_id_exact_terminal_benchmark");
        ids = Enumerable.Range(1, OperationsPerInvoke)
            .Select(static value => new ExactTerminalBenchmarkId(value))
            .ToArray();

        SeedRows();
        ValidateExactRoute();
    }

    private Database<TypedIdExactTerminalBenchmarkDatabase> Database => databaseScope.Database;

    internal void PrepareCold()
    {
        Database.Provider.State.ClearCache();
        DataLinqMetrics.Reset();
    }

    internal void PrepareWarm()
    {
        Database.Provider.State.ClearCache();
        _ = ExecuteBatch();
        DataLinqMetrics.Reset();
    }

    internal int ExecuteBatch()
    {
        var checksum = 0;

        for (var index = 0; index < ids.Length; index++)
        {
            var id = ids[index];
            var row = (index & 1) == 0
                ? Database.Query().Rows.Single(candidate => candidate.Id == id)
                : Database.Query().Rows.SingleOrDefault(candidate => id == candidate.Id)
                  ?? throw new InvalidOperationException($"Typed-ID exact lookup unexpectedly missed '{id.Value}'.");

            checksum = unchecked(checksum + row.Id.Value);
        }

        return checksum;
    }

    internal BenchmarkTelemetryDeltaArtifact CaptureTelemetryDelta(
        TypedIdExactTerminalScenario scenario,
        string providerName)
    {
        if (scenario == TypedIdExactTerminalScenario.Warm)
            PrepareWarm();
        else
            PrepareCold();

        var before = DataLinqMetrics.Snapshot();
        _ = ExecuteBatch();
        var after = DataLinqMetrics.Snapshot();

        return BenchmarkContext.CreateDeltaArtifact(
            scenario == TypedIdExactTerminalScenario.Warm ? WarmDisplayName : ColdDisplayName,
            providerName,
            OperationsPerInvoke,
            before,
            after);
    }

    public void Dispose() => databaseScope.Dispose();

    private void SeedRows()
    {
        var sql = new StringBuilder(
            "INSERT INTO typed_id_exact_terminal_rows (id) VALUES ");
        for (var index = 0; index < ids.Length; index++)
        {
            if (index > 0)
                sql.Append(',');
            sql.Append('(').Append(ids[index].Value).Append(')');
        }

        var inserted = Database.Provider.DatabaseAccess.ExecuteNonQuery(sql.ToString());
        if (inserted != ids.Length)
        {
            throw new InvalidOperationException(
                $"Typed-ID exact benchmark inserted {inserted} rows; expected {ids.Length}.");
        }
    }

    private void ValidateExactRoute()
    {
        var id = ids[0];
        var query = Database.Query().Rows.Where(row => row.Id == id);
        var plan = ExpressionQueryPlanParser.Convert(
            Database.Provider.Metadata,
            query.Expression,
            typeof(TypedIdExactTerminalBenchmarkRow));
        var select = new QueryPlanSqlBuilder(plan, Database.Provider.ReadOnlyAccess)
            .BuildSelect<TypedIdExactTerminalBenchmarkRow>();
        var canonicalKey = select.Query.TryGetSimplePrimaryKey()
            ?? throw new InvalidOperationException(
                "Typed-ID exact benchmark predicate did not expose a simple primary key.");

        if (canonicalKey.GetValue(0) is not int providerId || providerId != id.Value)
        {
            throw new InvalidOperationException(
                "Typed-ID exact benchmark converter did not bind the model ID to its canonical Int32 provider key.");
        }

        PrepareCold();
        var cold = Database.Query().Rows.Single(row => row.Id == id);
        var coldSnapshot = DataLinqMetrics.Snapshot();

        DataLinqMetrics.Reset();
        var warm = Database.Query().Rows.SingleOrDefault(row => id == row.Id)
            ?? throw new InvalidOperationException("Typed-ID warm exact route unexpectedly missed its row.");
        var warmSnapshot = DataLinqMetrics.Snapshot();

        if (cold.PrimaryKeys().GetValue(0) is not int materializedProviderId ||
            materializedProviderId != id.Value ||
            !ReferenceEquals(cold, warm) ||
            coldSnapshot.Queries.EntityExecutions != 1 ||
            coldSnapshot.Commands.ReaderExecutions != 1 ||
            coldSnapshot.RowCache.Hits != 0 ||
            coldSnapshot.RowCache.Misses != 1 ||
            coldSnapshot.RowCache.Stores != 1 ||
            coldSnapshot.RowCache.DatabaseRowsLoaded != 1 ||
            warmSnapshot.Queries.EntityExecutions != 1 ||
            warmSnapshot.Commands.ReaderExecutions != 0 ||
            warmSnapshot.RowCache.Hits != 1 ||
            warmSnapshot.RowCache.Misses != 0 ||
            warmSnapshot.RowCache.Stores != 0 ||
            warmSnapshot.RowCache.DatabaseRowsLoaded != 0)
        {
            throw new InvalidOperationException(
                "Typed-ID exact benchmark preflight did not observe the required cold/warm exact-route telemetry.");
        }

        PrepareCold();
    }
}
