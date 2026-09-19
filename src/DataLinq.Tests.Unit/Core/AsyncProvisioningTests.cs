using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Query;
using DataLinq.Tests.Unit.Fixtures;
using Microsoft.Extensions.Logging;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class AsyncProvisioningTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static int registrationId = 71000;
    private static DatabaseDefinition Metadata() => new("destination", new("Database", "Tests", ModelCsType.Class));

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NontransactionalCorrelation_ProvisioningDoesNotInventOrImportProviderIdentity(bool reused)
    {
        var factory = new ProvisioningFactory();
        var primary = new Exception("provisioning command");
        var cleanup = new Exception("provisioning session cleanup");
        var previous = new ExecutionFailureContext(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [], operation: ExecutionOperationKind.RawCommand,
            providerInstanceId: "previous-provider");
        if (reused) ExecutionFailureContexts.Attach(primary, previous);
        var dispatch = new AsyncCheckpoint(paused: true);
        dispatch.Fail(primary);
        factory.Session.Access = new ControlledAsyncDatabaseAccess(dispatch);
        factory.Session.Cleanup = new(paused: true);
        factory.Session.Cleanup.Fail(cleanup);
        var failure = await Fails(() => Create(factory));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Provisioning);
        await Assert.That(context.ProviderInstanceId).IsNull();
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(cleanup);
        await Assert.That(context.SecondaryFailures[0].Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(previous.ProviderInstanceId).IsEqualTo("previous-provider");
        await Assert.That(previous.Operation).IsEqualTo(ExecutionOperationKind.RawCommand);
    }

    [Test]
    [Arguments("factory")]
    [Arguments("sql")]
    [Arguments("database")]
    [Arguments("connection")]
    public async Task NullValidationPrecedesCancellationAndCapture(string argument)
    {
        var factory = new ProvisioningFactory();
        var failure = await Fails(() => AsyncProvisioning.CreateDatabaseAsyncCore(argument == "factory" ? null! : factory,
            argument == "sql" ? null! : new Sql("script"), argument == "database" ? null! : "database",
            argument == "connection" ? null! : "connection", true, new(true)));
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
        await Assert.That(factory.Captures).IsEqualTo(0);
        await Assert.That(factory.Plan.Creates).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LegacyFactoryRejectsAsyncBeforeCancellationWithoutSyncFallback(bool registered)
    {
        var factory = new LegacyFactory();
        var type = Register(factory);
        var failure = await Fails(() => registered
            ? type.CreateDatabaseFromSqlAsyncCore(new Sql("script"), "database", "connection", false, new(true))
            : factory.CreateDatabaseAsyncCore(new Sql("script"), "database", "connection", false, new(true)));
        await Assert.That(failure).IsTypeOf<NotSupportedException>();
        await Assert.That(factory.SyncCreates).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingRegistrationRemainsOptionFailureBeforeCancellation(bool metadata)
    {
        var type = (DatabaseType)Interlocked.Increment(ref registrationId);
        var result = await (metadata
            ? type.CreateDatabaseFromMetadataAsyncCore(Metadata(), "database", "connection", false, new(true))
            : type.CreateDatabaseFromSqlAsyncCore(new Sql("script"), "database", "connection", false, new(true)));
        await Assert.That(result.HasFailed).IsTrue();
    }

    [Test]
    public async Task GenerationFailureRetainsOptionIdentityWithoutCaptureOrExecution()
    {
        var expected = DLOptionFailure.Fail(DLFailureType.InvalidModel, "invalid generation input");
        var factory = new ProvisioningFactory { GenerationFailure = expected };
        var result = await Register(factory).CreateDatabaseFromMetadataAsyncCore(Metadata(), "database", "connection", true, new(true));
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(factory.Generations).IsEqualTo(1);
        await Assert.That(factory.Captures).IsEqualTo(0);
        await Assert.That(factory.Plan.Creates).IsEqualTo(0);
    }

    [Test]
    public async Task RegistryReplacementDuringGenerationDoesNotRedirectCapturedFactory()
    {
        var original = new ProvisioningFactory();
        var replacement = new ProvisioningFactory();
        var type = Register(original);
        original.Generating = () => Replace(type, replacement);
        var result = await type.CreateDatabaseFromMetadataAsyncCore(Metadata(), "database", "connection", true);
        await Assert.That(result.Value).IsEqualTo(7);
        await Assert.That(original.Generations).IsEqualTo(1);
        await Assert.That(original.Captures).IsEqualTo(1);
        await Assert.That(replacement.Generations).IsEqualTo(0);
        await Assert.That(replacement.Captures).IsEqualTo(0);
        await Assert.That(original.Request!.ForeignKeyRestrict).IsTrue();
        await Assert.That(original.GenerationForeignKeys).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InputAndRegistryCaptureSurvivesSuspendedInitialization(bool metadata)
    {
        var original = new ProvisioningFactory();
        original.Session.Initialization = new(paused: true);
        var type = Register(original);
        var sql = original.GeneratedSql;
        // Existing provisioning consumes only Text. Parameter cloning/dispatch
        // would silently turn this into a different, parameterized script API.
        sql.AddParameter("unused", new byte[] { 1 });
        var pending = metadata
            ? type.CreateDatabaseFromMetadataAsyncCore(Metadata(), "database", "connection", true)
            : type.CreateDatabaseFromSqlAsyncCore(sql, "database", "connection", true);
        await original.Session.Initialization.Entered.WaitAsync(Timeout);
        var replacement = new ProvisioningFactory();
        Replace(type, replacement);
        sql.AddText(" changed");
        sql.Parameters.Clear();
        var replacementAccess = new ControlledAsyncDatabaseAccess { NonQueryResult = 99 };
        original.Session.Access = replacementAccess;
        original.Session.CommandFactory = new ControlledOwnedCommandFactory();
        original.Session.Initialization.Release();
        var result = await pending.WaitAsync(Timeout);
        await Assert.That(result.Value).IsEqualTo(7);
        await Assert.That(original.Request!.Script).IsEqualTo("original script");
        await Assert.That(original.Request.DatabaseName).IsEqualTo("database");
        await Assert.That(original.Request.ConnectionString).IsEqualTo("connection");
        await Assert.That(original.Owned.Resource.Borrowed.CommandText).IsEqualTo("original script");
        // The probe command rejects all parameter collection access. Reaching a
        // result proves no parameterized-script execution was smuggled in.
        await Assert.That(replacement.Captures).IsEqualTo(0);
        await Assert.That(replacementAccess.Calls).IsEmpty();
        await Assert.That(original.Owned.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(original.Session.Disposals).IsEqualTo(1);
        await Assert.That(original.SyncCreates).IsEqualTo(0);
        await Assert.That(original.Owned.Resource.Borrowed.SyncExecutionCalls).IsEqualTo(0);
        await Assert.That(original.Owned.Resource.SyncDisposals).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PlanValidationAndPreCancellationPreventCreation(bool invalid)
    {
        var factory = new ProvisioningFactory();
        var expected = new NotSupportedException("unsupported provisioning cleanup");
        if (invalid) factory.Plan.ValidationFailure = expected;
        var failure = await Fails(() => factory.CreateDatabaseAsyncCore(new Sql("script"), "database", "connection", false, new(true)));
        if (invalid) await Assert.That(failure).IsSameReferenceAs(expected);
        else await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(factory.Plan.Validations).IsEqualTo(1);
        await Assert.That(factory.Plan.Creates).IsEqualTo(0);
        await Assert.That(factory.Session.Initializes).IsEqualTo(0);
        await Assert.That(factory.Session.Disposals).IsEqualTo(0);
    }

    [Test]
    public async Task CommandCapabilityValidationCleansSessionBeforeAnyInitialization()
    {
        var factory = new ProvisioningFactory();
        var expected = factory.Owned.ValidationFailure = new NotSupportedException("unsupported script command");
        var failure = await Fails(() => Create(factory));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(factory.Session.Initializes).IsEqualTo(0);
        await Assert.That(factory.Session.Disposals).IsEqualTo(1);
        await Assert.That(factory.Owned.Creates).IsEqualTo(0);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Stage).IsEqualTo(ExecutionFailureStage.Validation);
    }

    [Test]
    [Arguments("initialization", false)]
    [Arguments("initialization", true)]
    [Arguments("command", false)]
    [Arguments("command", true)]
    public async Task FailureOrCancellationPreservesPartialEffectsAndWaitsForAllOwnedCleanup(string phase, bool cancel)
    {
        var factory = new ProvisioningFactory();
        var checkpoint = new AsyncCheckpoint(paused: true);
        if (phase == "initialization") factory.Session.Initialization = checkpoint;
        else factory.Session.NativeAccess = new ControlledAsyncDatabaseAccess(checkpoint) { NonQueryResult = 7 };
        factory.Session.Cleanup = new(paused: true);
        if (phase == "command") factory.Owned.Resource.Cleanup = new(paused: true);
        using var cancellation = new CancellationTokenSource();
        var expected = new InvalidOperationException("original operational failure");
        var commandCleanup = new InvalidOperationException("command cleanup");
        var connectionCleanup = new InvalidOperationException("connection cleanup");
        var pending = Create(factory, cancellation.Token);
        await checkpoint.Entered.WaitAsync(Timeout);
        await Assert.That(checkpoint.ObservedToken).IsEqualTo(cancellation.Token);
        if (cancel) cancellation.Cancel(); else checkpoint.Fail(expected);
        if (phase == "command")
        {
            await factory.Owned.Resource.Cleanup.Entered.WaitAsync(Timeout);
            try
            {
                await Assert.That(pending.IsCompleted).IsFalse();
                await Assert.That(factory.Session.Disposals).IsEqualTo(0);
                await Assert.That(factory.Owned.Resource.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
            }
            finally { factory.Owned.Resource.Cleanup.Fail(commandCleanup); }
        }
        await factory.Session.Cleanup.Entered.WaitAsync(Timeout);
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(factory.Session.ResourcesLive).IsTrue();
            await Assert.That(factory.Session.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        }
        finally { factory.Session.Cleanup.Fail(connectionCleanup); }
        var failure = await Fails(() => pending);
        if (cancel) await Assert.That(failure is OperationCanceledException).IsTrue();
        else await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Stage).IsEqualTo(phase == "initialization" ? ExecutionFailureStage.Initialization : ExecutionFailureStage.CommandExecution);
        await Assert.That(context.Cause).IsEqualTo(cancel ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(phase == "command" ? 2 : 1);
        if (phase == "command") await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(commandCleanup);
        await Assert.That(context.SecondaryFailures[^1].Exception).IsSameReferenceAs(connectionCleanup);
        await Assert.That(factory.Session.CreatedDestination).IsTrue();
        await Assert.That(factory.Session.ResourcesLive).IsFalse();
        await Assert.That(factory.Plan.Creates).IsEqualTo(1);
        await Assert.That(factory.Session.Initializes).IsEqualTo(1);
        await Assert.That(factory.Session.Disposals).IsEqualTo(1);
        await Assert.That(factory.SyncCreates).IsEqualTo(0);
        await Assert.That(factory.Owned.Resource.SyncDisposals).IsEqualTo(0);
    }

    [Test]
    public async Task CancellationAfterInitializationPreventsCommandWithoutUndoingDestination()
    {
        var factory = new ProvisioningFactory();
        using var cancellation = new CancellationTokenSource();
        factory.Session.AfterInitialization = cancellation.Cancel;
        await Assert.That(await Fails(() => Create(factory, cancellation.Token)) is OperationCanceledException).IsTrue();
        await Assert.That(factory.Session.CreatedDestination).IsTrue();
        await Assert.That(factory.Owned.Creates).IsEqualTo(0);
        await Assert.That(factory.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConfirmedCommandResultWaitsForCleanupAndIsNotRetroactivelyCanceled(bool cleanupFails)
    {
        var factory = new ProvisioningFactory();
        factory.Owned.Resource.Cleanup = new(paused: true);
        factory.Session.Cleanup = new(paused: true);
        using var cancellation = new CancellationTokenSource();
        var pending = Create(factory, cancellation.Token);
        await factory.Owned.Resource.Cleanup.Entered.WaitAsync(Timeout);
        cancellation.Cancel();
        factory.Owned.Resource.Cleanup.Release();
        await factory.Session.Cleanup.Entered.WaitAsync(Timeout);
        var expected = new InvalidOperationException("cleanup prevents normal result");
        try { await Assert.That(pending.IsCompleted).IsFalse(); }
        finally { if (cleanupFails) factory.Session.Cleanup.Fail(expected); else factory.Session.Cleanup.Release(); }
        if (cleanupFails) await Assert.That(await Fails(() => pending)).IsSameReferenceAs(expected);
        else await Assert.That((await pending.WaitAsync(Timeout)).Value).IsEqualTo(7);
        await Assert.That(factory.Session.CreatedDestination).IsTrue();
    }

    [Test]
    [Arguments(false, "input")]
    [Arguments(false, "database")]
    [Arguments(false, "connection")]
    [Arguments(true, "input")]
    [Arguments(true, "database")]
    [Arguments(true, "connection")]
    public async Task RegisteredEntryPointsValidateNullsBeforeGenerationOrCancellation(bool metadata, string argument)
    {
        var factory = new ProvisioningFactory();
        var type = Register(factory);
        var failure = await Fails(() => metadata
            ? type.CreateDatabaseFromMetadataAsyncCore(argument == "input" ? null! : Metadata(),
                argument == "database" ? null! : "database", argument == "connection" ? null! : "connection", false, new(true))
            : type.CreateDatabaseFromSqlAsyncCore(argument == "input" ? null! : new Sql("script"),
                argument == "database" ? null! : "database", argument == "connection" ? null! : "connection", false, new(true)));
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
        await Assert.That(factory.Generations).IsEqualTo(0);
        await Assert.That(factory.Captures).IsEqualTo(0);
    }

    [Test]
    public async Task FailedSessionConstructionDoesNotRetryOrPretendOwnershipWasTransferred()
    {
        var factory = new ProvisioningFactory();
        var expected = factory.Plan.CreationFailure = new InvalidOperationException("unopened resource construction");
        await Assert.That(await Fails(() => Create(factory))).IsSameReferenceAs(expected);
        await Assert.That(factory.Plan.Creates).IsEqualTo(1);
        await Assert.That(factory.Session.Initializes).IsEqualTo(0);
        await Assert.That(factory.Session.Disposals).IsEqualTo(0);
        await Assert.That(factory.Owned.Creates).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InvalidSessionCapabilityStillCleansItsOwnedResources(bool missingAccess)
    {
        var factory = new ProvisioningFactory();
        if (missingAccess) factory.Session.Access = null!; else factory.Session.CommandFactory = null!;
        await Assert.That(await Fails(() => Create(factory))).IsTypeOf<ArgumentNullException>();
        await Assert.That(factory.Session.Initializes).IsEqualTo(0);
        await Assert.That(factory.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task RepeatedExceptionObjectIsNotDuplicatedAcrossNestedCleanup()
    {
        var factory = new ProvisioningFactory();
        var dispatch = new AsyncCheckpoint(paused: true);
        factory.Session.NativeAccess = new(dispatch);
        var expected = new InvalidOperationException("same operation and cleanup failure");
        factory.Owned.Resource.Cleanup = new(paused: true);
        factory.Owned.Resource.Cleanup.Fail(expected);
        factory.Session.Cleanup = new(paused: true);
        factory.Session.Cleanup.Fail(expected);
        var pending = Create(factory);
        await dispatch.Entered.WaitAsync(Timeout);
        dispatch.Fail(expected);
        await Assert.That(await Fails(() => pending)).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(expected)!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(0);
        await Assert.That(factory.Owned.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(factory.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task NonCooperativeInitializationRemainsOwnedAfterRequestCancellation()
    {
        var factory = new ProvisioningFactory();
        factory.Session.Initialization = new(paused: true);
        factory.Session.IgnoreInitializationCancellation = true;
        using var cancellation = new CancellationTokenSource();
        var pending = Create(factory, cancellation.Token);
        await factory.Session.Initialization.Entered.WaitAsync(Timeout);
        cancellation.Cancel();
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(factory.Session.Disposals).IsEqualTo(0);
            await Assert.That(factory.Session.InitializationToken).IsEqualTo(cancellation.Token);
        }
        finally { factory.Session.Initialization.Release(); }
        await Assert.That(await Fails(() => pending) is OperationCanceledException).IsTrue();
        await Assert.That(factory.Owned.Creates).IsEqualTo(0);
        await Assert.That(factory.Session.Disposals).IsEqualTo(1);
        await Assert.That(factory.Session.CreatedDestination).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommandConstructionFailureAfterInitializationRetainsEffectsAndReleasesOwnedResources(bool afterCommandTransfer)
    {
        var factory = new ProvisioningFactory();
        var expected = new InvalidOperationException("command construction or handoff");
        factory.Session.AfterInitialization = () =>
        {
            if (afterCommandTransfer) factory.Owned.Resource.CommandFailure = expected;
            else factory.Owned.Creating = () => throw expected;
        };
        await Assert.That(await Fails(() => Create(factory))).IsSameReferenceAs(expected);
        await Assert.That(factory.Session.CreatedDestination).IsTrue();
        await Assert.That(factory.Session.Disposals).IsEqualTo(1);
        await Assert.That(factory.Owned.Resource.AsyncDisposals).IsEqualTo(afterCommandTransfer ? 1 : 0);
        await Assert.That(factory.Owned.Creates).IsEqualTo(1);
        await Assert.That(factory.Owned.Resource.Borrowed.SyncExecutionCalls).IsEqualTo(0);
        var context = ExecutionFailureContexts.Get(expected)!;
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
    }

    private static Task<Option<int, IDLOptionFailure>> Create(ProvisioningFactory factory, CancellationToken token = default) =>
        factory.CreateDatabaseAsyncCore(new Sql("script"), "database", "connection", true, token);

    private static async Task<Exception> Fails(Func<Task> action)
    {
        try { await action().WaitAsync(Timeout); }
        catch (Exception failure) { return failure; }
        throw new InvalidOperationException("Expected failure.");
    }

    private static DatabaseType Register(ISqlFromMetadataFactory factory)
    {
        var type = (DatabaseType)Interlocked.Increment(ref registrationId);
        Replace(type, factory);
        return type;
    }

    private static void Replace(DatabaseType type, ISqlFromMetadataFactory factory) =>
        PluginHook.RegisterProvider(type, new UnusedServices(), factory, new UnusedServices(), replaceExisting: true);

    private class LegacyFactory : ISqlFromMetadataFactory
    {
        internal int SyncCreates { get; private set; }
        internal int Generations { get; private set; }
        internal bool GenerationForeignKeys { get; private set; }
        internal Sql GeneratedSql { get; } = new("original script");
        internal IDLOptionFailure? GenerationFailure { get; set; }
        internal Action? Generating { get; set; }
        public Option<int, IDLOptionFailure> CreateDatabase(Sql sql, string databaseName, string connectionString, bool foreignKeyRestrict)
        { SyncCreates++; throw new InvalidOperationException("Synchronous provisioning must not execute."); }
        public Option<Sql, IDLOptionFailure> GetCreateTables(DatabaseDefinition metadata, bool foreignKeyRestrict)
        {
            Generations++;
            GenerationForeignKeys = foreignKeyRestrict;
            Generating?.Invoke();
            if (GenerationFailure is not null) return GenerationFailure;
            return GeneratedSql;
        }
    }

    private sealed class ProvisioningFactory : LegacyFactory, IAsyncSqlProvisioningFactory
    {
        internal ProvisioningFactory() { Session = new(Owned); Plan = new(Session); }
        internal ControlledOwnedCommandFactory Owned { get; } = new();
        internal ProvisioningSession Session { get; }
        internal ProvisioningPlan Plan { get; }
        internal ProvisioningRequest? Request { get; private set; }
        internal int Captures { get; private set; }
        public IAsyncProvisioningPlan CaptureProvisioning(ProvisioningRequest request)
        {
            Captures++;
            Request = request;
            Owned.Creating = () => { Owned.Resource.Borrowed.CommandText = request.Script; return Owned.Resource; };
            return Plan;
        }
    }

    private sealed class ProvisioningPlan(ProvisioningSession session) : IAsyncProvisioningPlan
    {
        internal int Validations { get; private set; }
        internal int Creates { get; private set; }
        internal Exception? ValidationFailure { get; set; }
        internal Exception? CreationFailure { get; set; }
        public void Validate() { Validations++; if (ValidationFailure is not null) throw ValidationFailure; }
        public IAsyncProvisioningSession CreateSession() { Creates++; if (CreationFailure is not null) throw CreationFailure; return session; }
    }

    private sealed class ProvisioningSession(ControlledOwnedCommandFactory owned) : IAsyncProvisioningSession
    {
        public IAsyncDatabaseAccess Access { get; set; } = new ControlledAsyncDatabaseAccess { NonQueryResult = 7 };
        internal ControlledAsyncDatabaseAccess NativeAccess { set => Access = value; }
        public IAsyncOwnedCommandFactory CommandFactory { get; set; } = owned;
        internal AsyncCheckpoint Initialization { get; set; } = new();
        internal AsyncCheckpoint Cleanup { get; set; } = new();
        internal Action? AfterInitialization { get; set; }
        internal bool IgnoreInitializationCancellation { get; set; }
        internal CancellationToken InitializationToken { get; private set; }
        internal int Initializes { get; private set; }
        internal int Disposals { get; private set; }
        internal bool ResourcesLive { get; private set; }
        internal bool CreatedDestination { get; private set; }
        public async Task InitializeAsync(CancellationToken token)
        {
            Initializes++;
            ResourcesLive = true;
            CreatedDestination = true;
            InitializationToken = token;
            await Initialization.ReachAsync(IgnoreInitializationCancellation ? CancellationToken.None : token);
            AfterInitialization?.Invoke();
        }
        public async ValueTask DisposeAsync()
        {
            Disposals++;
            try { await Cleanup.ReachAsync(CancellationToken.None); }
            finally { ResourcesLive = false; }
        }
    }

    private sealed class UnusedServices : IDatabaseProviderCreator, IMetadataFromDatabaseFactoryCreator
    {
        public Database<T> GetDatabaseProvider<T>(string connectionString, string databaseName) where T : class, IDatabaseModel<T> => throw new NotSupportedException();
        public bool IsDatabaseType(string typeName) => false;
        public IDatabaseProviderCreator UseLoggerFactory(ILoggerFactory? loggerFactory) => this;
        public IMetadataFromSqlFactory GetMetadataFromSqlFactory(MetadataFromDatabaseFactoryOptions options) => throw new NotSupportedException();
    }
}
