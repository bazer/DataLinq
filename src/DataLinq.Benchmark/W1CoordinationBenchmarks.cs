using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using DataLinq.Execution;
using DataLinq.Metadata;
using Perfolizer.Horology;
using Perfolizer.Metrology;

namespace DataLinq.Benchmark;

/// <summary>
/// Bounded internal costs, not native-provider latency or a frozen W0 comparison lane.
/// A minimal cursor is a measurement control, never a production fallback.
/// </summary>
[Config(typeof(W1CoordinationBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("w1-coordination-diagnostic")]
public class W1CoordinationBenchmarks
{
    private const int PrimitiveOperations = 256;
    private readonly EnumeratorCallGate calls = new();
    private readonly TransactionOperationGate transactionGate = new(1, "controlled");
    private readonly CancellationTokenSource firstToken = new();
    private readonly CancellationTokenSource secondToken = new();
    private Scenario selected;
    private ReaderEvidence lastReader;

    [Params("controlled")]
    public string ProviderName { get; set; } = "controlled";

    private enum Scenario { None, Scope, NestedScopes, Enumerator, Transaction, Helper, DirectOne, CoordinatedOne,
        DirectMany, CoordinatedMany, DirectSuspending, CoordinatedSuspending, SharedToken, LinkedTokens }
    private enum Tokens { None, Shared, Linked }

    [GlobalSetup]
    public async Task Setup()
    {
        if (ProviderName != "controlled") throw new InvalidOperationException("This is a controlled, non-provider workload.");
        // Verify the workload before measurement. Each measured reader invocation
        // also verifies its checksum, exact resource counts and suspension protocol.
        foreach (var scenario in Enum.GetValues<Scenario>().Where(value => value != Scenario.None))
            await Execute(scenario).ConfigureAwait(false);
        selected = Scenario.None;
    }

    [Benchmark(Description = "W1 diagnostic scope", OperationsPerInvoke = PrimitiveOperations)]
    public int DiagnosticScope()
    {
        selected = Scenario.Scope;
        for (var i = 0; i < PrimitiveOperations; i++)
        {
            using var scope = ExecutionFailureScope.Begin();
        }
        return PrimitiveOperations;
    }

    [Benchmark(Description = "W1 nested diagnostic scopes", OperationsPerInvoke = PrimitiveOperations)]
    public int NestedDiagnosticScopes()
    {
        selected = Scenario.NestedScopes;
        for (var i = 0; i < PrimitiveOperations; i++)
        {
            using var outer = ExecutionFailureScope.Begin();
            using var inner = ExecutionFailureScope.Begin();
        }
        return PrimitiveOperations;
    }

    [Benchmark(Description = "W1 enumerator admission", OperationsPerInvoke = PrimitiveOperations)]
    public int EnumeratorAdmission()
    {
        selected = Scenario.Enumerator;
        for (var i = 0; i < PrimitiveOperations; i++)
        {
            using var call = calls.Enter();
        }
        return PrimitiveOperations;
    }

    [Benchmark(Description = "W1 transaction admission", OperationsPerInvoke = PrimitiveOperations)]
    public int TransactionAdmission()
    {
        selected = Scenario.Transaction;
        for (var i = 0; i < PrimitiveOperations; i++)
        {
            using var lease = transactionGate.Enter("controlled read", operationKind: ExecutionOperationKind.Query);
            using var step = transactionGate.EnterStep(lease);
            transactionGate.ValidateStep(step);
        }
        return PrimitiveOperations;
    }

    [Benchmark(Description = "W1 empty helper lifetime", OperationsPerInvoke = PrimitiveOperations)]
    public async Task<int> EmptyHelperLifetime()
    {
        selected = Scenario.Helper;
        for (var i = 0; i < PrimitiveOperations; i++)
        {
            // Includes a fresh gate, helper and failure collector. No callback,
            // provider transaction, recovery, active reader or commit is simulated.
            var gate = new TransactionOperationGate(1, "controlled");
            var helper = gate.BeginHelperLifetime();
            var failures = new ExecutionFailures();
            using var completion = await helper.CloseAndDrainAsync(failures).ConfigureAwait(false);
            failures.ThrowIfAny();
        }
        return PrimitiveOperations;
    }

    [Benchmark(Description = "W1 direct reader one row")]
    public Task<int> DirectOneRow() => RunReader(Scenario.DirectOne, false, 1);
    [Benchmark(Description = "W1 coordinated reader one row")]
    public Task<int> CoordinatedOneRow() => RunReader(Scenario.CoordinatedOne, true, 1);
    [Benchmark(Description = "W1 direct reader sixteen rows")]
    public Task<int> DirectSixteenRows() => RunReader(Scenario.DirectMany, false, 16);
    [Benchmark(Description = "W1 coordinated reader sixteen rows")]
    public Task<int> CoordinatedSixteenRows() => RunReader(Scenario.CoordinatedMany, true, 16);
    [Benchmark(Description = "W1 direct reader suspended")]
    public Task<int> DirectSuspending() => RunReader(Scenario.DirectSuspending, false, 1, suspend: true);
    [Benchmark(Description = "W1 coordinated reader suspended")]
    public Task<int> CoordinatedSuspending() => RunReader(Scenario.CoordinatedSuspending, true, 1, suspend: true);
    [Benchmark(Description = "W1 coordinated reader shared token")]
    public Task<int> CoordinatedSharedToken() => RunReader(Scenario.SharedToken, true, 1, tokens: Tokens.Shared);
    [Benchmark(Description = "W1 coordinated reader linked tokens")]
    public Task<int> CoordinatedLinkedTokens() => RunReader(Scenario.LinkedTokens, true, 1, tokens: Tokens.Linked);

    private async Task<int> RunReader(Scenario scenario, bool coordinated, int rows, bool suspend = false, Tokens tokens = Tokens.None)
    {
        selected = scenario;
        var source = new ControlledReader(rows, suspend);
        var methodToken = tokens == Tokens.None ? default : firstToken.Token;
        var enumerationToken = tokens == Tokens.Linked ? secondToken.Token : methodToken;
        IAsyncEnumerator<int> reader = coordinated
            ? new AsyncReaderEnumerable<int>(() => source, static current => current.GetInt32(0), cancellationToken: methodToken)
                .GetAsyncEnumerator(enumerationToken)
            : new DirectCursor(source);
        var checksum = 0;
        var suspensions = 0;
        try
        {
            while (true)
            {
                var move = reader.MoveNextAsync();
                if (suspend)
                {
                    // Release only after the real coordinator/control has returned
                    // an incomplete move. This proves suspension without Task.Run,
                    // sleeps, timer races or assuming Task.Yield was still pending.
                    source.ReleasePending(move.IsCompleted);
                    suspensions++;
                }
                if (!await move.ConfigureAwait(false)) break;
                checksum += reader.Current;
            }
        }
        finally { await reader.DisposeAsync().ConfigureAwait(false); }

        if (checksum != rows * (rows + 1) / 2 || source.Validations != 1 || source.Opens != 1 ||
            source.Reads != rows + 1 || source.Values != rows || source.Disposals != 1 ||
            suspensions != (suspend ? rows + 1 : 0) || source.HasPendingRead)
            throw new InvalidOperationException("The controlled workload did not complete its exact result/resource protocol.");
        var tokenMatches = tokens switch
        {
            Tokens.None => !source.Token.CanBeCanceled,
            Tokens.Shared => source.Token == firstToken.Token,
            Tokens.Linked => source.Token.CanBeCanceled && source.Token != firstToken.Token && source.Token != secondToken.Token,
            _ => false
        };
        if (!tokenMatches) throw new InvalidOperationException("The reader received the wrong cancellation-token shape.");
        lastReader = new(rows, checksum, source.Validations, source.Opens, source.Reads, source.Values,
            source.Disposals, suspensions, tokens switch { Tokens.None => "none", Tokens.Shared => "shared", _ => "linked" });
        return checksum;
    }

    private Task<int> Execute(Scenario scenario) => scenario switch
    {
        Scenario.Scope => Task.FromResult(DiagnosticScope()),
        Scenario.NestedScopes => Task.FromResult(NestedDiagnosticScopes()),
        Scenario.Enumerator => Task.FromResult(EnumeratorAdmission()),
        Scenario.Transaction => Task.FromResult(TransactionAdmission()),
        Scenario.Helper => EmptyHelperLifetime(),
        Scenario.DirectOne => DirectOneRow(), Scenario.CoordinatedOne => CoordinatedOneRow(),
        Scenario.DirectMany => DirectSixteenRows(), Scenario.CoordinatedMany => CoordinatedSixteenRows(),
        Scenario.DirectSuspending => DirectSuspending(), Scenario.CoordinatedSuspending => CoordinatedSuspending(),
        Scenario.SharedToken => CoordinatedSharedToken(), Scenario.LinkedTokens => CoordinatedLinkedTokens(),
        _ => throw new InvalidOperationException("No workload was measured.")
    };

    [GlobalCleanup]
    public async Task Cleanup()
    {
        try
        {
            var scenario = selected;
            _ = await Execute(scenario).ConfigureAwait(false);
            var method = scenario switch
            {
                Scenario.Scope => "W1 diagnostic scope", Scenario.NestedScopes => "W1 nested diagnostic scopes",
                Scenario.Enumerator => "W1 enumerator admission", Scenario.Transaction => "W1 transaction admission",
                Scenario.Helper => "W1 empty helper lifetime", Scenario.DirectOne => "W1 direct reader one row",
                Scenario.CoordinatedOne => "W1 coordinated reader one row", Scenario.DirectMany => "W1 direct reader sixteen rows",
                Scenario.CoordinatedMany => "W1 coordinated reader sixteen rows", Scenario.DirectSuspending => "W1 direct reader suspended",
                Scenario.CoordinatedSuspending => "W1 coordinated reader suspended", Scenario.SharedToken => "W1 coordinated reader shared token",
                Scenario.LinkedTokens => "W1 coordinated reader linked tokens", _ => throw new InvalidOperationException()
            };
            var primitive = scenario <= Scenario.Helper;
            var operations = primitive ? PrimitiveOperations : 1;
            // The ordinary telemetry dimensions represent actual database/model work:
            // all are zero here. Keep synthetic protocol counts in a separate block.
            BenchmarkTelemetryDeltaWriter.TryWrite(new BenchmarkTelemetryDeltaArtifact(
                Method: method, ProviderName: ProviderName, OperationsPerInvoke: operations,
                EntityQueriesPerOperation: 0, ScalarQueriesPerOperation: 0,
                TransactionStartsPerOperation: 0, TransactionCommitsPerOperation: 0, TransactionRollbacksPerOperation: 0,
                MutationInsertsPerOperation: 0, MutationUpdatesPerOperation: 0, MutationDeletesPerOperation: 0, MutationAffectedRowsPerOperation: 0,
                RowCacheHitsPerOperation: 0, RowCacheMissesPerOperation: 0, RowCacheStoresPerOperation: 0,
                DatabaseRowsPerOperation: 0, MaterializationsPerOperation: 0, RelationHitsPerOperation: 0, RelationLoadsPerOperation: 0,
                CacheInvalidationOperationsPerOperation: 0, CacheInvalidationRowsRemovedPerOperation: 0, CacheInvalidationTablesClearedPerOperation: 0,
                CacheInvalidationProviderKeysPerOperation: 0, CacheInvalidationApproximateWorkPerOperation: 0,
                CacheInvalidationPreciseOperationsPerOperation: 0, CacheInvalidationConservativeFallbackOperationsPerOperation: 0),
                new { Scope = "controlled internal coordination; no database I/O", OperationsPerInvoke = operations,
                    CompletedPrimitiveCycles = primitive ? operations : 0,
                    Reader = primitive ? (ReaderEvidence?)null : lastReader });
        }
        finally { firstToken.Dispose(); secondToken.Dispose(); }
    }

    private readonly record struct ReaderEvidence(int Rows, int Checksum, int Validations, int Opens, int Reads,
        int Values, int AsyncDisposals, int ForcedSuspensions, string Tokens);

    private sealed class DirectCursor(ControlledReader source) : IAsyncEnumerator<int>
    {
        private bool started;
        private bool finished;
        private bool hasCurrent;
        public int Current => hasCurrent ? source.GetInt32(0) : throw new InvalidOperationException();
        public async ValueTask<bool> MoveNextAsync()
        {
            if (finished) return false;
            hasCurrent = false;
            if (!started)
            {
                source.Validate();
                _ = await source.OpenReaderAsync(default).ConfigureAwait(false);
                started = true;
            }
            if (await source.ReadNextRowAsync(default).ConfigureAwait(false)) return hasCurrent = true;
            await DisposeAsync().ConfigureAwait(false);
            return false;
        }
        public ValueTask DisposeAsync()
        {
            if (finished) return default;
            finished = true;
            hasCurrent = false;
            return started ? source.DisposeAsync() : default;
        }
    }

    private sealed class ControlledReader(int rowCount, bool suspend) : IAsyncReaderSource, IAsyncDataReader
    {
        private TaskCompletionSource<bool>? pending;
        private bool pendingValue;
        private int position;
        internal int Validations { get; private set; }
        internal int Opens { get; private set; }
        internal int Reads { get; private set; }
        internal int Values { get; private set; }
        internal int Disposals { get; private set; }
        internal CancellationToken Token { get; private set; }
        internal bool HasPendingRead => pending is not null;
        public void Validate() => Validations++;
        public Task<IAsyncDataReader> OpenReaderAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Opens++ != 0 || Validations != 1) throw new InvalidOperationException("Invalid open protocol.");
            Token = cancellationToken;
            return Task.FromResult<IAsyncDataReader>(this);
        }
        public Task<bool> ReadNextRowAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cancellationToken != Token || Opens != 1 || Disposals != 0 || pending is not null)
                throw new InvalidOperationException("Invalid read protocol.");
            Reads++;
            pendingValue = ++position <= rowCount;
            if (!suspend) return Task.FromResult(pendingValue);
            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return pending.Task;
        }
        internal void ReleasePending(bool moveCompleted)
        {
            if (moveCompleted || pending is null || pending.Task.IsCompleted)
                throw new InvalidOperationException("The move did not suspend at its controlled read.");
            var released = pending;
            pending = null;
            released.SetResult(pendingValue);
        }
        public int GetInt32(int ordinal)
        {
            if (ordinal != 0 || position < 1 || position > rowCount || Disposals != 0 || pending is not null)
                throw new InvalidOperationException("No current row.");
            Values++;
            return position;
        }
        public ValueTask DisposeAsync()
        {
            if (pending is not null || Disposals++ != 0) throw new InvalidOperationException("Invalid disposal protocol.");
            return default;
        }
        public void Dispose() => throw new InvalidOperationException("Synchronous disposal was used.");
        public bool ReadNextRow() => throw new InvalidOperationException("Synchronous advancement was used.");
        public object GetValue(int ordinal) => GetInt32(ordinal);
        public int GetOrdinal(string name) => name == "value" ? 0 : throw new ArgumentException(nameof(name));
        public bool IsDbNull(int ordinal) => false;
        public string GetString(int ordinal) => throw new NotSupportedException();
        public bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public DateOnly GetDateOnly(int ordinal) => throw new NotSupportedException();
        public Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public byte[]? GetBytes(int ordinal) => throw new NotSupportedException();
        public long GetBytes(int ordinal, Span<byte> buffer) => throw new NotSupportedException();
        public T? GetValue<T>(ColumnDefinition column) => throw new NotSupportedException();
        public T? GetValue<T>(ColumnDefinition column, int ordinal) => throw new NotSupportedException();
    }
}

internal sealed class W1CoordinationBenchmarkConfig : ManualConfig
{
    public W1CoordinationBenchmarkConfig()
    {
        Add(new DataLinqBenchmarkConfig());
        // Keep the frozen lanes' display unchanged. Small diagnostic allocations
        // need byte units rather than two-decimal KiB rounding.
        WithSummaryStyle(SummaryStyle.Default.WithTimeUnit(TimeUnit.Microsecond)
            .WithSizeUnit(SizeUnit.B).WithMaxParameterColumnWidth(24));
    }
}
