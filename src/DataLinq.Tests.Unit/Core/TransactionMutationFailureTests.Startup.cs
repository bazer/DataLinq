using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.ErrorHandling;
using DataLinq.Exceptions;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Extensions.Hosting;
using DataLinq.Metadata;
using DataLinq.Tests.Unit.Fixtures;
using DataLinq.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ThrowAway;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Startup_CanonicalAliasesAndBothFluentOrdersValidateOnce(bool defaultFirst)
    {
        using var target = new StartupFixture();
        var services = new ServiceCollection();
        var defaults = 0;
        var overrides = 0;
        var activations = 0;
        services.AddDataLinqSchemaValidation(_ => defaults++)
            .ValidateDatabase<TransactionMutationGuardDb>()
            .ValidateDatabase<TransactionMutationGuardDb>("primary");
        var builder = services.AddDataLinq<TransactionMutationGuardDb>("primary");
        if (defaultFirst) builder.AsDefault();
        builder.UseFactory(_ => { activations++; return target.Database; }).ValidateSchemaOnStartup(_ => overrides++);
        if (!defaultFirst) builder.AsDefault();
        using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var adapter = StartupAdapter(container);
        await Assert.That(activations).IsEqualTo(0);
        var first = adapter.StartingAsync(default);
        await first;
        await Assert.That(adapter.StartingAsync(default)).IsSameReferenceAs(first);
        await Assert.That(defaults).IsEqualTo(1);
        await Assert.That(overrides).IsEqualTo(1);
        await Assert.That(activations).IsEqualTo(1);
        await Assert.That(target.Reads).IsEqualTo(1);
        await Assert.That(container.GetRequiredService<Database<TransactionMutationGuardDb>>()).IsSameReferenceAs(target.Database);
    }

    [Test]
    public async Task Startup_GlobalDisableSkipsTargetResolutionCallbacksAndFactories()
    {
        var services = new ServiceCollection();
        var activations = 0;
        services.AddDataLinq<TransactionMutationGuardDb>().UseFactory<ScriptedDatabase>(_ =>
        { activations++; throw new InvalidOperationException("missing secret must not be requested"); });
        services.AddDataLinqSchemaValidation(options => options.Mode = DataLinqSchemaStartupMode.Disabled)
            .ValidateDatabase<TransactionMutationGuardDb>("unregistered", _ => throw new InvalidOperationException("target callback must not run"));
        using var container = services.BuildServiceProvider();
        await StartupAdapter(container).StartingAsync(default);
        await Assert.That(activations).IsEqualTo(0);
    }

    [Test]
    public async Task Startup_DuplicateExplicitOwnerRejectsBeforeCallbacksOrActivation()
    {
        var services = new ServiceCollection();
        var calls = 0;
        services.AddDataLinq<TransactionMutationGuardDb>("primary").AsDefault()
            .UseFactory<ScriptedDatabase>(_ => { calls++; throw new InvalidOperationException(); })
            .ValidateSchemaOnStartup(_ => calls++);
        services.AddDataLinqSchemaValidation().ValidateDatabase<TransactionMutationGuardDb>(configure: _ => calls++);
        using var container = services.BuildServiceProvider();
        var failure = await Assert.That(() => StartupAdapter(container).StartingAsync(default)).Throws<DataLinqSchemaStartupException>();
        await Assert.That(failure!.Outcomes).IsEmpty();
        await Assert.That(failure.RunFailure).IsTypeOf<InvalidOperationException>();
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    [Arguments("no-target")]
    [Arguments("missing-default")]
    [Arguments("missing-key")]
    [Arguments("mode")]
    [Arguments("timeout")]
    [Arguments("include")]
    public async Task Startup_PreflightRejectsInvalidSelectionsAndSettingsWithoutActivation(string problem)
    {
        var services = new ServiceCollection();
        var activations = 0;
        services.AddDataLinq<TransactionMutationGuardDb>("registered").UseFactory<ScriptedDatabase>(_ =>
        { activations++; throw new InvalidOperationException("must not activate"); });
        var builder = services.AddDataLinqSchemaValidation(options =>
        {
            if (problem == "mode") options.Mode = (DataLinqSchemaStartupMode)99;
            if (problem == "timeout") options.Validation.CommandTimeout = TimeSpan.FromTicks(-1);
            if (problem == "include") options.Validation.Include = [" "];
        });
        if (problem != "no-target") builder.ValidateDatabase<TransactionMutationGuardDb>(problem switch
        { "missing-default" => null, "missing-key" => "missing", _ => "registered" });
        using var container = services.BuildServiceProvider();
        var failure = await Assert.That(() => StartupAdapter(container).StartingAsync(default)).Throws<DataLinqSchemaStartupException>();
        await Assert.That(failure!.RunFailure).IsNotNull();
        await Assert.That(failure.Outcomes).IsEmpty();
        await Assert.That(activations).IsEqualTo(0);
    }

    [Test]
    public async Task Startup_AllOptionsFreezeBeforeActivationAndComplexKeysNeverFormat()
    {
        using var first = new StartupFixture();
        using var second = new StartupFixture();
        var events = new List<string>();
        var key = new UnformattableStartupKey(1);
        var services = new ServiceCollection();
        DataLinqSchemaStartupOptions? defaults = null;
        DataLinqSchemaStartupOptions? firstOptions = null;
        services.AddDataLinqSchemaValidation(options => { defaults = options; options.Validation.CommandTimeout = TimeSpan.FromSeconds(3); })
            .ValidateDatabase<TransactionMutationGuardDb>(new UnformattableStartupKey(1), options =>
            { events.Add("configure-first"); firstOptions = options; options.Validation.CommandTimeout = TimeSpan.FromSeconds(1); })
            .ValidateDatabase<TransactionMutationGuardDb>("second", options =>
            {
                events.Add("configure-second");
                firstOptions!.Validation.CommandTimeout = TimeSpan.FromSeconds(-1);
                defaults!.Validation.CommandTimeout = TimeSpan.FromSeconds(-1);
                options.Validation.CommandTimeout = TimeSpan.FromSeconds(2);
            });
        services.AddDataLinq<TransactionMutationGuardDb>("second").UseFactory(_ => { events.Add("activate-second"); return second.Database; });
        services.AddDataLinq<TransactionMutationGuardDb>(key).UseFactory(_ => { events.Add("activate-first"); return first.Database; });
        using var container = services.BuildServiceProvider();
        await StartupAdapter(container).StartingAsync(default);
        await Assert.That(events.SequenceEqual(["configure-first", "configure-second", "activate-first", "activate-second"])).IsTrue();
        await Assert.That(first.Harness.Settings!.CommandTimeoutSeconds).IsEqualTo(1);
        await Assert.That(second.Harness.Settings!.CommandTimeoutSeconds).IsEqualTo(2);
    }

    [Test]
    [Arguments(DataLinqSchemaStartupMode.FailStartup, false, false)]
    [Arguments(DataLinqSchemaStartupMode.FailStartup, true, false)]
    [Arguments(DataLinqSchemaStartupMode.WarningOnly, false, false)]
    [Arguments(DataLinqSchemaStartupMode.WarningOnly, true, false)]
    [Arguments(DataLinqSchemaStartupMode.FailStartup, false, true)]
    [Arguments(DataLinqSchemaStartupMode.WarningOnly, false, true)]
    [Arguments(DataLinqSchemaStartupMode.Disabled, true, true)]
    public async Task Startup_ModeTruthTable(DataLinqSchemaStartupMode mode, bool drift, bool operational)
    {
        using var target = new StartupFixture { Drift = drift };
        var original = new InvalidOperationException("original independent read failure");
        if (operational) target.FailOpening(original);
        var services = new ServiceCollection();
        var activations = 0;
        services.AddDataLinq<TransactionMutationGuardDb>().UseFactory(_ => { activations++; return target.Database; })
            .ValidateSchemaOnStartup(options => options.Mode = mode);
        using var container = services.BuildServiceProvider();
        if (mode != DataLinqSchemaStartupMode.Disabled && (operational || mode == DataLinqSchemaStartupMode.FailStartup && drift))
        {
            var failure = await Assert.That(() => StartupAdapter(container).StartingAsync(default)).Throws<DataLinqSchemaStartupException>();
            await Assert.That(failure!.RunFailure).IsNull();
            await Assert.That(failure.Outcomes.Count).IsEqualTo(1);
            if (operational)
            {
                await Assert.That(failure.Outcomes[0].OperationalFailure).IsSameReferenceAs(original);
                await Assert.That(failure.InnerException).IsSameReferenceAs(original);
                await Assert.That(failure.Outcomes[0].Result).IsNull();
            }
            else await Assert.That(failure.Outcomes[0].Result!.HasFailures).IsTrue();
        }
        else await StartupAdapter(container).StartingAsync(default);
        await Assert.That(activations).IsEqualTo(mode == DataLinqSchemaStartupMode.Disabled ? 0 : 1);
        await Assert.That(target.Harness.Session.Disposals).IsEqualTo(mode == DataLinqSchemaStartupMode.Disabled ? 0 : 1);
    }

    [Test]
    [Arguments(DataLinqSchemaStartupMode.FailStartup)]
    [Arguments(DataLinqSchemaStartupMode.WarningOnly)]
    public async Task Startup_MixedOutcomesContinueOnlyAfterPriorSettlement(DataLinqSchemaStartupMode mode)
    {
        using var first = new StartupFixture { Drift = true };
        using var second = new StartupFixture();
        using var third = new StartupFixture();
        var original = new InvalidOperationException("provider failure with original details");
        second.FailOpening(original);
        second.Harness.Session.Cleanup = new(paused: true);
        var services = new ServiceCollection();
        services.AddDataLinqSchemaValidation(options => options.Mode = mode)
            .ValidateDatabase<TransactionMutationGuardDb>("first").ValidateDatabase<TransactionMutationGuardDb>("second").ValidateDatabase<TransactionMutationGuardDb>("third");
        services.AddDataLinq<TransactionMutationGuardDb>("first").UseInstance(first.Database);
        services.AddDataLinq<TransactionMutationGuardDb>("second").UseInstance(second.Database);
        services.AddDataLinq<TransactionMutationGuardDb>("third").UseInstance(third.Database);
        using var container = services.BuildServiceProvider();
        var pending = StartupAdapter(container).StartingAsync(default);
        await second.Harness.Session.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(third.Provider.Captures).IsEqualTo(0);
        await Assert.That(pending.IsCompleted).IsFalse();
        second.Harness.Session.Cleanup.Release();
        var failure = await Assert.That(async () => await pending).Throws<DataLinqSchemaStartupException>();
        await Assert.That(failure!.Outcomes.Select(outcome => outcome.Target.ServiceKey).SequenceEqual(new object[] { "first", "second", "third" })).IsTrue();
        await Assert.That(failure.Outcomes[0].Result!.HasFailures).IsTrue();
        await Assert.That(failure.Outcomes[0].PreventsStartup).IsEqualTo(mode == DataLinqSchemaStartupMode.FailStartup);
        await Assert.That(failure.Outcomes[1].OperationalFailure).IsSameReferenceAs(original);
        await Assert.That(failure.Outcomes[2].Result!.HasFailures).IsFalse();
        await Assert.That(failure.InnerException).IsSameReferenceAs(original);
        await Assert.That(failure.RunFailure).IsNull();
        await Assert.That(() => ((IList<DataLinqSchemaStartupOutcome>)failure.Outcomes).Clear()).Throws<NotSupportedException>();
    }

    [Test]
    public async Task Startup_MetadataCallbackProviderExceptionAbortsAfterCleanupAndRetainsEarlierOutcome()
    {
        using var first = new StartupFixture();
        using var second = new StartupFixture();
        using var third = new StartupFixture();
        var original = new SqliteException("callback exception with provider type", 1);
        second.Harness.Session.Cleanup = new(paused: true);
        var services = new ServiceCollection();
        services.AddDataLinq<TransactionMutationGuardDb>("first").UseInstance(first.Database).ValidateSchemaOnStartup();
        services.AddDataLinq<TransactionMutationGuardDb>("second").UseInstance(second.Database)
            .ValidateSchemaOnStartup(options => options.Validation.MetadataReaderLog = _ => throw original);
        services.AddDataLinq<TransactionMutationGuardDb>("third").UseInstance(third.Database).ValidateSchemaOnStartup();
        using var container = services.BuildServiceProvider();
        var pending = StartupAdapter(container).StartingAsync(default);
        await second.Harness.Session.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(pending.IsCompleted).IsFalse();
        second.Harness.Session.Cleanup.Release();
        var failure = await Assert.That(async () => await pending).Throws<DataLinqSchemaStartupException>();
        await Assert.That(failure!.RunFailure).IsSameReferenceAs(original);
        await Assert.That(failure.Outcomes.Count).IsEqualTo(1);
        await Assert.That(failure.Outcomes[0].Result!.HasFailures).IsFalse();
        await Assert.That(third.Provider.Captures).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Startup_RealHostBarrierPrecedesWorkerStartRegardlessOfOrderOrConcurrency(bool concurrent, bool validationFirst)
    {
        using var target = new StartupFixture();
        target.Harness.Session.Opening = new(paused: true);
        target.Harness.Session.Cleanup = new(paused: true);
        var worker = new StartupWorker();
        using var host = new HostBuilder().ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices(services =>
        {
            services.Configure<HostOptions>(options => options.ServicesStartConcurrently = concurrent);
            if (!validationFirst) services.AddSingleton<IHostedService>(worker);
            services.AddDataLinq<TransactionMutationGuardDb>().UseInstance(target.Database).ValidateSchemaOnStartup();
            if (validationFirst) services.AddSingleton<IHostedService>(worker);
        }).Build();
        await Assert.That(target.Provider.Captures).IsEqualTo(0);
        var pending = host.StartAsync();
        await target.Harness.Session.Opening.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(worker.Starts).IsEqualTo(0);
        target.Harness.Session.Opening.Release();
        await target.Harness.Session.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(worker.Starts).IsEqualTo(0);
        target.Harness.Session.Cleanup.Release();
        await pending;
        await Assert.That(worker.Starts).IsEqualTo(1);
        await host.StopAsync();
    }

    private static IHostedLifecycleService StartupAdapter(IServiceProvider services) =>
        (IHostedLifecycleService)services.GetServices<IHostedService>().Single();

    private sealed class StartupFixture : IDisposable
    {
        internal MetadataHarness Harness { get; } = new([]);
        internal MetadataTestProvider Provider { get; }
        internal ScriptedDatabase Database { get; }
        internal bool Drift;
        internal int Reads;
        internal StartupFixture()
        {
            Provider = new(new(), () => Harness);
            Database = new(Provider);
            Harness.Parser = (_, _) =>
            {
                Reads++;
                Harness.Settings!.SetIdentifiers(SchemaIdentifierRules.SQLite);
                Harness.Settings.Log?.Invoke("original catalog diagnostic 'A' => 'a'");
                return Task.FromResult<Option<DatabaseDefinition, IDLOptionFailure>>(Drift ? Harness.EmptyDefinition() : Provider.Metadata);
            };
        }
        internal void FailOpening(Exception failure)
        { Harness.Session.Opening = new(paused: true); Harness.Session.Opening.Fail(failure); }
        public void Dispose() => Database.Dispose();
    }

    private sealed record UnformattableStartupKey(int Value)
    {
        public override string ToString() => throw new InvalidOperationException("arbitrary key formatting must not run");
    }

    private sealed class StartupWorker : IHostedService
    {
        internal int Starts;
        public Task StartAsync(CancellationToken cancellationToken) { Starts++; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
