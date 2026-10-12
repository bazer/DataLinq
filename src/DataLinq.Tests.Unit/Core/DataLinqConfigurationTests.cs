using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.MariaDB.Extensions.DependencyInjection;
using DataLinq.MySql.Extensions.DependencyInjection;
using DataLinq.SQLite;
using DataLinq.SQLite.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataLinq.Tests.Unit.Core;

public sealed class DataLinqConfigurationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BindingsPrecedeOrderedOverridesAndCaptureSettingsAndOptionalLogging(bool logging)
    {
        var configuration = Config(("ConnectionString", "Data Source=:memory:"),
            ("DatabaseName", "bound"), ("Execution:RecoveryRollbackTimeout", "00:00:02"));
        var services = new ServiceCollection();
        var logger = new CountingLoggerFactory();
        if (logging) services.AddSingleton<ILoggerFactory>(logger);
        var calls = 0;
        SQLiteRegistrationOptions? retained = null;
        var builder = services.AddDataLinq<TransactionMutationGuardDb>("primary").UseSQLite()
            .Configure(options =>
            {
                calls++;
                if (options.Execution.RecoveryRollbackTimeout != TimeSpan.FromSeconds(3)) throw new Exception("Code ran before binding.");
                options.DatabaseName = "first";
                retained = options;
            })
            .Bind(configuration)
            .Configure(options =>
            {
                if (options.DatabaseName != "first") throw new Exception("Code callback ordering changed.");
                options.DatabaseName = Guid.NewGuid().ToString("N");
                options.Execution = new() { RecoveryRollbackTimeout = TimeSpan.FromSeconds(7) };
            }).AsDefault();
        await using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await Assert.That(calls).IsEqualTo(0);
        await Assert.That(logger.Categories).IsEmpty();
        configuration["Execution:RecoveryRollbackTimeout"] = "00:00:03";
        var database = container.GetRequiredService<SQLiteDatabase<TransactionMutationGuardDb>>();
        var root = (SQLiteProvider<TransactionMutationGuardDb>)database.Provider;
        var databaseName = root.DatabaseName;
        await Assert.That(root.ExecutionOptions.RecoveryRollbackTimeout).IsEqualTo(TimeSpan.FromSeconds(7));
        await Assert.That(root.LoggingConfiguration.LoggerFactory).IsSameReferenceAs(logging ? logger : NullLoggerFactory.Instance);
        if (logging) await Assert.That(logger.Categories.Contains("DataLinq.Transaction")).IsTrue();
        retained!.DatabaseName = "changed";
        retained.ConnectionString = "invalid";
        retained.Execution = new() { RecoveryRollbackTimeout = TimeSpan.FromSeconds(99) };
        configuration["ConnectionString"] = "also invalid";
        await Assert.That(container.GetRequiredKeyedService<Database<TransactionMutationGuardDb>>("primary")).IsSameReferenceAs(database);
        await Assert.That(root.DatabaseName).IsEqualTo(databaseName);
        await Assert.That(root.ExecutionOptions.RecoveryRollbackTimeout).IsEqualTo(TimeSpan.FromSeconds(7));
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(() => builder.Configure(_ => { })).Throws<InvalidOperationException>();
        await Assert.That(() => builder.Bind(configuration)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task NamedConnectionsResolveLateAndEachKeyAndContainerGetsItsOwnSettings()
    {
        var config = Config(("ConnectionStrings:active", "invalid before resolution"));
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        var calls = 0;
        services.AddDataLinq<TransactionMutationGuardDb>("named").UseSQLiteConnectionString("active")
            .Configure(options => { calls++; options.DatabaseName = "named" + calls; });
        services.AddDataLinq<TransactionMutationGuardDb>("literal").UseSQLite("Data Source=:memory:")
            .Configure(options => options.Execution = new() { RecoveryRollbackTimeout = TimeSpan.FromSeconds(4) });
        await using var first = services.BuildServiceProvider();
        await using var second = services.BuildServiceProvider();
        config["ConnectionStrings:active"] = "Data Source=:memory:";
        var named = first.GetRequiredKeyedService<SQLiteDatabase<TransactionMutationGuardDb>>("named");
        var literal = first.GetRequiredKeyedService<SQLiteDatabase<TransactionMutationGuardDb>>("literal");
        await Assert.That(named.Provider.ExecutionOptions.RecoveryRollbackTimeout).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(literal.Provider.ExecutionOptions.RecoveryRollbackTimeout).IsEqualTo(TimeSpan.FromSeconds(4));
        config["ConnectionStrings:active"] = "Data Source=:memory:;Cache=Shared";
        var other = second.GetRequiredKeyedService<SQLiteDatabase<TransactionMutationGuardDb>>("named");
        await Assert.That(other.Provider.DatabaseName).IsEqualTo("named2");
        await Assert.That(named.Provider.DatabaseName).IsEqualTo("named1");
        await Assert.That(other).IsNotSameReferenceAs(named);
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExplicitConnectionSelectionReplacesAmbiguousBindingAsAWhole(bool named)
    {
        var config = Config(("ConnectionString", "sensitive invalid literal"), ("ConnectionStringName", "stale"),
            ("ConnectionStrings:selected", "Data Source=:memory:"), ("Execution:RecoveryRollbackTimeout", "invalid bound duration"));
        var services = new ServiceCollection();
        if (named) services.AddSingleton<IConfiguration>(config);
        var registration = services.AddDataLinq<TransactionMutationGuardDb>();
        var provider = named ? registration.UseSQLiteConnectionString("selected") : registration.UseSQLite("Data Source=:memory:");
        provider.Bind(config).Configure(options => options.Execution = new() { RecoveryRollbackTimeout = TimeSpan.FromMilliseconds(1) });
        await using var container = services.BuildServiceProvider();
        var root = container.GetRequiredService<SQLiteDatabase<TransactionMutationGuardDb>>().Provider;
        await Assert.That(root.ExecutionOptions.RecoveryRollbackTimeout).IsEqualTo(TimeSpan.FromMilliseconds(1));
    }

    [Test]
    [Arguments("SQLite")]
    [Arguments("MySql")]
    [Arguments("MariaDB")]
    public async Task InvalidConnectionDiagnosticsDoNotExposeSecretsOrConstructAProvider(string selectedProvider)
    {
        var services = new ServiceCollection();
        var registration = services.AddDataLinq<TransactionMutationGuardDb>();
        const string secret = "w4-synthetic-secret-never-print";
        var invalid = secret + "=value;Password=" + secret;
        if (selectedProvider == "SQLite") registration.UseSQLite(invalid);
        else if (selectedProvider == "MySql") registration.UseMySql(invalid);
        else registration.UseMariaDB(invalid);
        using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var failure = Capture(() => container.GetRequiredService<Database<TransactionMutationGuardDb>>());
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(failure.ToString().Contains(secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(failure.InnerException).IsNull();
    }

    [Test]
    [Arguments("missing-source")]
    [Arguments("ambiguous-source")]
    [Arguments("missing-configuration")]
    [Arguments("missing-name")]
    [Arguments("invalid-duration")]
    [Arguments("zero-duration")]
    [Arguments("negative-duration")]
    [Arguments("too-long-duration")]
    [Arguments("null-execution")]
    public async Task InvalidEffectiveSettingsFailBeforeSQLiteCreatesTheDatabase(string failureCase)
    {
        var path = Path.Combine(Path.GetTempPath(), "datalinq-w4-config-" + Guid.NewGuid().ToString("N") + ".db");
        var config = Config(("ConnectionString", "Data Source=" + path));
        var services = new ServiceCollection();
        var builder = services.AddDataLinq<TransactionMutationGuardDb>().UseSQLite().Bind(config);
        switch (failureCase)
        {
            case "missing-source": config["ConnectionString"] = " "; break;
            case "ambiguous-source": config["ConnectionStringName"] = "sensitive-name"; break;
            case "missing-configuration": builder.Configure(o => o.ConnectionStringName = "sensitive-name"); break;
            case "missing-name":
                services.AddSingleton<IConfiguration>(config);
                builder.Configure(o => o.ConnectionStringName = "sensitive-name"); break;
            case "invalid-duration": config["Execution:RecoveryRollbackTimeout"] = "sensitive-invalid-duration"; break;
            case "zero-duration": config["Execution:RecoveryRollbackTimeout"] = "00:00:00"; break;
            case "negative-duration": config["Execution:RecoveryRollbackTimeout"] = "-00:00:01"; break;
            case "too-long-duration": builder.Configure(o => o.Execution = new() { RecoveryRollbackTimeout = TimeSpan.FromMilliseconds(4294967295) }); break;
            case "null-execution": builder.Configure(o => o.Execution = null!); break;
        }
        using var container = services.BuildServiceProvider();
        await Assert.That(File.Exists(path)).IsFalse();
        var failure = Capture(() => container.GetRequiredService<IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb>>());
        await Assert.That(failure.ToString().Contains("sensitive-", StringComparison.Ordinal)).IsFalse();
        await Assert.That(File.Exists(path)).IsFalse();
    }

    [Test]
    public async Task RegistrationAndNeverResolvedShutdownDoNotCreateSQLiteFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), "datalinq-w4-lazy-" + Guid.NewGuid().ToString("N") + ".db");
        var services = new ServiceCollection();
        services.AddDataLinq<TransactionMutationGuardDb>().UseSQLite("Data Source=" + path + ";Pooling=False");
        await using (var unused = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }))
            await Assert.That(File.Exists(path)).IsFalse();
        await Assert.That(File.Exists(path)).IsFalse();
        try
        {
            await using (var used = services.BuildServiceProvider())
            {
                used.GetRequiredService<TransactionMutationGuardDb>();
                await Assert.That(File.Exists(path)).IsTrue();
            }
        }
        finally { File.Delete(path); }
    }

    private static IConfigurationRoot Config(params (string Key, string? Value)[] entries)
    {
        var data = new Dictionary<string, string?>();
        foreach (var entry in entries) data.Add(entry.Key, entry.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    private static Exception Capture(Action action)
    {
        try { action(); }
        catch (Exception failure) { return failure; }
        throw new Exception("Expected configuration activation to fail.");
    }

    private sealed class CountingLoggerFactory : ILoggerFactory
    {
        internal List<string> Categories { get; } = [];
        public ILogger CreateLogger(string categoryName) { Categories.Add(categoryName); return NullLogger.Instance; }
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }
}
