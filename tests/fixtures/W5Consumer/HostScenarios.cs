using DataLinq.Exceptions;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Extensions.Hosting;
using DataLinq.SQLite.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SessionConsumer;

namespace W5Consumer;

internal static class HostScenarios
{
    internal static async Task Run()
    {
        foreach (var mode in Enum.GetValues<DataLinqSchemaStartupMode>())
        foreach (var drift in new[] { false, true })
        foreach (var concurrent in new[] { false, true })
        foreach (var validationFirst in new[] { false, true })
        foreach (var scoped in new[] { false, true })
        {
            using var store = Fixture.Database();
            Fixture.CreateSchema(store);
            store.Provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE unrelated(id INTEGER PRIMARY KEY)");
            if (drift) store.Provider.DatabaseAccess.ExecuteNonQuery("ALTER TABLE rows RENAME COLUMN value TO wrong");
            var starts = new Worker();
            var log = new RecordingLog();
            var configured = 0;
            var targetConfigured = 0;
            using var host = new HostBuilder().ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:AppDatabase"] = store.Provider.ConnectionString }))
                .ConfigureLogging(logging => logging.ClearProviders().AddProvider(log)).ConfigureServices(services =>
                {
                    services.Configure<HostOptions>(options => options.ServicesStartConcurrently = concurrent);
                    if (!validationFirst) services.AddSingleton<IHostedService>(starts);
                    services.AddDataLinqSchemaValidation(options => options.Mode = mode);
                    services.AddDataLinq<TestDb>("selected").UseSQLiteConnectionString("AppDatabase")
                        .Configure(options => { configured++; options.DatabaseName = store.Provider.DatabaseName; })
                        .AsDefault().ValidateSchemaOnStartup(options => { targetConfigured++; options.Validation.Include = scoped ? ["rows"] : null; });
                    // All these selections refer to the one configured target.
                    services.AddDataLinqSchemaValidation().ValidateDatabase<TestDb>().ValidateDatabase<TestDb>("selected");
                    if (validationFirst) services.AddSingleton<IHostedService>(starts);
                }).Build();
            Fixture.Check(configured == 0 && targetConfigured == 0, "Building a host activated validation.");
            if (drift && mode == DataLinqSchemaStartupMode.FailStartup)
            {
                var failure = await Fixture.ExpectAsync<DataLinqSchemaStartupException>(() => host.StartAsync());
                Bindings.Inspect(failure);
                Fixture.Check(starts.Starts == 0 && failure.Outcomes.Count == 1 && failure.Outcomes[0].Result!.HasFailures,
                    "Failed comparison did not block ordinary worker startup.");
                Fixture.Check(log.Exceptions.Any(error => ReferenceEquals(error, failure)), "Normal host logging lost the startup exception.");
            }
            else
            {
                await host.StartAsync();
                Fixture.Check(starts.Starts == 1, "Allowed startup did not start the worker.");
                await host.StopAsync();
            }
            Fixture.Check(configured == (mode == DataLinqSchemaStartupMode.Disabled ? 0 : 1) && configured == targetConfigured,
                "Canonical registration was configured repeatedly or while disabled.");
            if (drift && mode != DataLinqSchemaStartupMode.Disabled)
                Fixture.Check(log.Messages.Any(message => message.Contains("value", StringComparison.Ordinal)), "Drift path was lost in logs.");
            if (mode != DataLinqSchemaStartupMode.Disabled)
                Fixture.Check(log.Messages.Any(message => message.Contains($"1 model objects, {(scoped ? 1 : 2)} database objects", StringComparison.Ordinal)),
                    "Host comparison did not retain its full/Include scope and counts.");
        }
        // An application factory error is a run-level failure even when it has
        // a provider-like exception type; WarningOnly cannot permit startup.
        var original = new Microsoft.Data.Sqlite.SqliteException("callback details 'A' => 'a'", 1);
        using var failing = new HostBuilder().ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices(services =>
        {
            services.AddDataLinq<TestDb>().UseFactory<DataLinq.SQLite.SQLiteDatabase<TestDb>>(_ => throw original)
                .ValidateSchemaOnStartup(options => options.Mode = DataLinqSchemaStartupMode.WarningOnly);
        }).Build();
        var rejected = await Fixture.ExpectAsync<DataLinqSchemaStartupException>(() => failing.StartAsync());
        Fixture.Check(ReferenceEquals(rejected.RunFailure, original) && rejected.Outcomes.Count == 0, "Factory failure was misclassified or replaced.");
        Console.WriteLine("Generic Host: 48 mode/order/concurrency/scope cases and callback provenance passed.");
    }

    internal sealed class Worker : IHostedService
    {
        internal int Starts;
        public Task StartAsync(CancellationToken cancellationToken) { Starts++; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    internal sealed class RecordingLog : ILoggerProvider, ILogger
    {
        internal List<string> Messages { get; } = [];
        internal List<Exception?> Exceptions { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { lock (Messages) { Messages.Add(formatter(state, exception)); Exceptions.Add(exception); } }
        public void Dispose() { }
    }
}
