using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Extensions.Hosting;
using DataLinq.SQLite.Extensions.DependencyInjection;
using DataLinq.Tests.Unit.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    public async Task Startup_MultipleOperationalFailuresKeepTargetOrderAndNestedCleanupEvidence()
    {
        using var first = new StartupFixture();
        using var second = new StartupFixture();
        var firstFailure = new InvalidOperationException("first provider failure");
        var cleanup = new ApplicationException("first cleanup failure");
        var secondFailure = new InvalidOperationException("second provider failure");
        first.FailOpening(firstFailure);
        first.Harness.Session.Cleanup = new(paused: true);
        first.Harness.Session.Cleanup.Fail(cleanup);
        second.FailOpening(secondFailure);
        var services = new ServiceCollection();
        services.AddDataLinq<TransactionMutationGuardDb>("first").UseInstance(first.Database).ValidateSchemaOnStartup();
        services.AddDataLinq<TransactionMutationGuardDb>("second").UseInstance(second.Database).ValidateSchemaOnStartup();
        using var container = services.BuildServiceProvider();
        var failure = await Assert.That(() => StartupAdapter(container).StartingAsync(default)).Throws<DataLinqSchemaStartupException>();
        var nested = (DataLinqSchemaReadException)failure!.Outcomes[0].OperationalFailure!;
        await Assert.That(nested.Exceptions[0]).IsSameReferenceAs(firstFailure);
        await Assert.That(nested.Exceptions[1]).IsSameReferenceAs(cleanup);
        await Assert.That(failure.Outcomes[1].OperationalFailure).IsSameReferenceAs(secondFailure);
        var aggregate = (AggregateException)failure.InnerException!;
        await Assert.That(aggregate.InnerExceptions[0]).IsSameReferenceAs(nested);
        await Assert.That(aggregate.InnerExceptions[1]).IsSameReferenceAs(secondFailure);
        await Assert.That(failure.RunFailure).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Startup_DisabledProviderBuilderDoesNotResolveConnectionNamesOrConfigure(bool global)
    {
        var services = new ServiceCollection();
        var calls = 0;
        if (global) services.AddDataLinqSchemaValidation(options => options.Mode = DataLinqSchemaStartupMode.Disabled);
        services.AddDataLinq<TransactionMutationGuardDb>("native").UseSQLiteConnectionString("intentionally-unavailable")
            .ValidateSchemaOnStartup(options =>
            {
                if (global) throw new InvalidOperationException("global disablement must skip this callback");
                options.Mode = DataLinqSchemaStartupMode.Disabled;
            }).AsDefault().Configure(_ => calls++);
        using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        await StartupAdapter(container).StartingAsync(default);
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task Startup_MissingConnectionConfigurationAbortsBeforeLaterTargets()
    {
        using var later = new StartupFixture();
        var services = new ServiceCollection();
        services.AddDataLinq<TransactionMutationGuardDb>("missing").UseSQLiteConnectionString("intentionally-unavailable").ValidateSchemaOnStartup();
        services.AddDataLinq<TransactionMutationGuardDb>("later").UseInstance(later.Database).ValidateSchemaOnStartup();
        using var container = services.BuildServiceProvider();
        var failure = await Assert.That(() => StartupAdapter(container).StartingAsync(default)).Throws<DataLinqSchemaStartupException>();
        await Assert.That(failure!.RunFailure).IsNotNull();
        await Assert.That(failure.Outcomes).IsEmpty();
        await Assert.That(later.Provider.Captures).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, DataLinqSchemaStartupMode.FailStartup)]
    [Arguments(true, DataLinqSchemaStartupMode.FailStartup)]
    [Arguments(false, DataLinqSchemaStartupMode.WarningOnly)]
    [Arguments(true, DataLinqSchemaStartupMode.WarningOnly)]
    public async Task Startup_RealHostPolicyAndFrameworkLogsPreserveComparisonFailure(bool concurrent, DataLinqSchemaStartupMode mode)
    {
        using var target = new StartupFixture { Drift = true };
        var worker = new StartupWorker();
        var logger = new StartupLogger();
        using var host = new HostBuilder().ConfigureLogging(logging => logging.ClearProviders()
            .AddProvider(new StartupLoggerProvider(logger)).SetMinimumLevel(LogLevel.Trace))
            .ConfigureServices(services =>
            {
                services.Configure<HostOptions>(options => options.ServicesStartConcurrently = concurrent);
                services.AddSingleton<IHostedService>(worker);
                services.AddDataLinq<TransactionMutationGuardDb>().UseInstance(target.Database)
                    .ValidateSchemaOnStartup(options => options.Mode = mode);
            }).Build();
        if (mode == DataLinqSchemaStartupMode.FailStartup)
        {
            var failure = await Assert.That(() => host.StartAsync()).Throws<DataLinqSchemaStartupException>();
            await Assert.That(failure!.Outcomes.Single().Result!.HasFailures).IsTrue();
            await Assert.That(worker.Starts).IsEqualTo(0);
            // The adapter does not log its own aggregate. This is the real
            // framework retaining the ordinary exception object and chaining.
            await Assert.That(logger.Entries.Any(entry => ReferenceEquals(entry.Exception, failure))).IsTrue();
        }
        else
        {
            await host.StartAsync();
            await Assert.That(worker.Starts).IsEqualTo(1);
            await host.StopAsync();
        }
        await Assert.That(logger.Entries.Any(entry => entry.Values.TryGetValue("HasFailures", out var value) && Equals(value, true))).IsTrue();
    }

    [Test]
    public async Task Startup_LoggerFailureStopsSchedulingWithoutRecursiveLogging()
    {
        using var first = new StartupFixture();
        using var second = new StartupFixture();
        var original = new InvalidOperationException("original logger failure");
        var logger = new StartupLogger { Failure = original };
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<SchemaValidationHostedService>>(logger);
        services.AddDataLinq<TransactionMutationGuardDb>("first").UseInstance(first.Database).ValidateSchemaOnStartup();
        services.AddDataLinq<TransactionMutationGuardDb>("second").UseInstance(second.Database).ValidateSchemaOnStartup();
        using var container = services.BuildServiceProvider();
        var failure = await Assert.That(() => StartupAdapter(container).StartingAsync(default)).Throws<DataLinqSchemaStartupException>();
        await Assert.That(failure!.RunFailure).IsSameReferenceAs(original);
        await Assert.That(failure.InnerException).IsSameReferenceAs(original);
        await Assert.That(failure.Outcomes.Count).IsEqualTo(1);
        await Assert.That(failure.Outcomes[0].Result!.HasFailures).IsFalse();
        await Assert.That(logger.Calls).IsEqualTo(1);
        await Assert.That(second.Provider.Captures).IsEqualTo(0);
    }

    [Test]
    public async Task Startup_LogsRetainEveryDifferenceAndOriginalOperationalText()
    {
        using var first = new StartupFixture { Drift = true };
        using var second = new StartupFixture();
        var original = new InvalidOperationException("Server=literal-test-server;Password=literal-test-value; 'A' differs from 'a'");
        second.FailOpening(original);
        var logger = new StartupLogger();
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<SchemaValidationHostedService>>(logger);
        services.AddDataLinq<TransactionMutationGuardDb>("first").UseInstance(first.Database).ValidateSchemaOnStartup();
        services.AddDataLinq<TransactionMutationGuardDb>("second").UseInstance(second.Database).ValidateSchemaOnStartup();
        using var container = services.BuildServiceProvider();
        var failure = await Assert.That(() => StartupAdapter(container).StartingAsync(default)).Throws<DataLinqSchemaStartupException>();
        foreach (var difference in failure!.Outcomes[0].Result!.Differences)
            await Assert.That(logger.Entries.Any(entry => entry.Values.TryGetValue("Message", out var value) && Equals(value, difference.Message))).IsTrue();
        var operational = logger.Entries.Single(entry => ReferenceEquals(entry.Exception, original));
        await Assert.That((string)operational.Values["Message"]!).IsEqualTo(original.Message);
        await Assert.That(operational.Text).Contains(original.Message);
        await Assert.That(operational.Values["Target"]!.ToString()!).Contains("second");
    }

    [Test]
    [Arguments("factory")]
    [Arguments("provider-configure")]
    public async Task Startup_ConfigurationOriginOverridesProviderExceptionType(string source)
    {
        using var first = new StartupFixture();
        using var third = new StartupFixture();
        var original = new SqliteException("provider-shaped callback failure", 1);
        var services = new ServiceCollection();
        services.AddDataLinq<TransactionMutationGuardDb>("first").UseInstance(first.Database).ValidateSchemaOnStartup();
        var second = services.AddDataLinq<TransactionMutationGuardDb>("second");
        if (source == "factory") second.UseFactory<ScriptedDatabase>(_ => throw original).ValidateSchemaOnStartup();
        else second.UseSQLite().Configure(_ => throw original).ValidateSchemaOnStartup();
        services.AddDataLinq<TransactionMutationGuardDb>("third").UseInstance(third.Database).ValidateSchemaOnStartup();
        using var container = services.BuildServiceProvider();
        var failure = await Assert.That(() => StartupAdapter(container).StartingAsync(default)).Throws<DataLinqSchemaStartupException>();
        await Assert.That(failure!.RunFailure).IsSameReferenceAs(original);
        await Assert.That(failure.Outcomes.Count).IsEqualTo(1);
        await Assert.That(third.Provider.Captures).IsEqualTo(0);
    }

    [Test]
    public async Task Startup_CancellationSettlesCurrentTargetAndNeverActivatesTheNext()
    {
        using var first = new StartupFixture();
        using var second = new StartupFixture();
        first.Harness.Session.Cleanup = new AsyncCheckpoint(paused: true);
        var services = new ServiceCollection();
        services.AddDataLinq<TransactionMutationGuardDb>("first").UseInstance(first.Database).ValidateSchemaOnStartup();
        services.AddDataLinq<TransactionMutationGuardDb>("second").UseInstance(second.Database).ValidateSchemaOnStartup();
        using var container = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var pending = StartupAdapter(container).StartingAsync(cancellation.Token);
        await first.Harness.Session.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.That(pending.IsCompleted).IsFalse();
        first.Harness.Session.Cleanup.Release();
        var failure = await Assert.That(async () => await pending).Throws<OperationCanceledException>();
        await Assert.That(failure!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(first.Harness.Session.Disposals).IsEqualTo(1);
        await Assert.That(second.Provider.Captures).IsEqualTo(0);
    }

    [Test]
    public async Task Startup_CancellationFromTheFinalLogStillCancelsStartup()
    {
        using var target = new StartupFixture();
        using var cancellation = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<SchemaValidationHostedService>>(new StartupLogger { Logging = cancellation.Cancel });
        services.AddDataLinq<TransactionMutationGuardDb>().UseInstance(target.Database).ValidateSchemaOnStartup();
        using var container = services.BuildServiceProvider();
        var failure = await Assert.That(() => StartupAdapter(container).StartingAsync(cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(failure!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(target.Harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task Startup_SeparateContainersCaptureAndValidateIndependently()
    {
        var targets = new List<StartupFixture>();
        var defaults = 0;
        var services = new ServiceCollection();
        services.AddDataLinqSchemaValidation(_ => defaults++).ValidateDatabase<TransactionMutationGuardDb>();
        services.AddDataLinq<TransactionMutationGuardDb>().UseFactory(_ =>
        { var target = new StartupFixture(); targets.Add(target); return target.Database; });
        using (var first = services.BuildServiceProvider()) await StartupAdapter(first).StartingAsync(default);
        using (var second = services.BuildServiceProvider()) await StartupAdapter(second).StartingAsync(default);
        await Assert.That(defaults).IsEqualTo(2);
        await Assert.That(targets.Count).IsEqualTo(2);
        await Assert.That(targets.All(target => target.Reads == 1)).IsTrue();
    }

    [Test]
    public async Task Startup_DistinctRegistrationsSharingABorrowedProviderAreIndependentTargets()
    {
        using var target = new StartupFixture();
        var services = new ServiceCollection();
        services.AddDataLinq<TransactionMutationGuardDb>("one").UseInstance(target.Database).ValidateSchemaOnStartup();
        services.AddDataLinq<TransactionMutationGuardDb>("two").UseInstance(target.Database).ValidateSchemaOnStartup();
        using (var container = services.BuildServiceProvider()) await StartupAdapter(container).StartingAsync(default);
        await Assert.That(target.Reads).IsEqualTo(2);
        await Assert.That(target.Database.Query()).IsNotNull();
    }

    private sealed record StartupLogEntry(string Text, Dictionary<string, object?> Values, Exception? Exception);
    private sealed class StartupLoggerProvider(StartupLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;
        public void Dispose() { }
    }
    private sealed class StartupLogger : ILogger<SchemaValidationHostedService>
    {
        internal int Calls;
        internal Exception? Failure;
        internal Action? Logging;
        internal readonly List<StartupLogEntry> Entries = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Calls++;
            if (Failure is not null) throw Failure;
            Entries.Add(new(formatter(state, exception), state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(item => item.Key, item => item.Value) : [], exception));
            Logging?.Invoke();
        }
    }
}
