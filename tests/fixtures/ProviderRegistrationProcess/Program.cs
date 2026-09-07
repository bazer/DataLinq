using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataLinq;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MariaDB;
using DataLinq.SQLite;
using DataLinq.Tests.Models.Employees;
using DataLinq.Tests.Models.GeneratedDefaults;
namespace DataLinq.Tests.Fixtures;

public static class ProviderRegistrationProcess
{
    public static int Main()
    {
        try
        {
            Check(PluginHook.Registrations.Count == 0, "Fixture must start with empty registries.");
            RaceFirstUse();
            CheckSnapshotsAndReplacement();
            Console.WriteLine("registration checks passed");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void RaceFirstUse()
    {
        Action[] register =
        [
            () => RuntimeHelpers.RunClassConstructor(typeof(SQLiteProvider<EmployeesDb>).TypeHandle),
            () => RuntimeHelpers.RunClassConstructor(typeof(SQLiteProvider<GeneratedDefaultDb>).TypeHandle),
            () => RuntimeHelpers.RunClassConstructor(typeof(MySqlProvider<EmployeesDb>).TypeHandle),
            () => RuntimeHelpers.RunClassConstructor(typeof(MySqlProvider<GeneratedDefaultDb>).TypeHandle),
            () => RuntimeHelpers.RunClassConstructor(typeof(MariaDBProvider<EmployeesDb>).TypeHandle),
            () => RuntimeHelpers.RunClassConstructor(typeof(MariaDBProvider<GeneratedDefaultDb>).TypeHandle),
            SQLiteProvider.RegisterProvider, MySQLProvider.RegisterProvider, MariaDBProvider.RegisterProvider
        ];
        using var start = new Barrier(register.Length + 1);
        using var stopObserver = new CancellationTokenSource();
        var observer = Task.Factory.StartNew(() =>
        {
            if (!start.SignalAndWait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Observer start barrier timed out.");
            while (!stopObserver.IsCancellationRequested)
                CheckCompleteRegistrations();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var workers = register.Select(action => Task.Factory.StartNew(() =>
        {
            if (!start.SignalAndWait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Registration start barrier timed out.");
            for (var i = 0; i < 1000; i++)
            {
                action();
                CheckCompleteRegistrations();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        try
        {
            Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        }
        finally
        {
            stopObserver.Cancel();
            observer.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
        Check(PluginHook.Registrations.Count == 3, "Expected all three provider families.");
        Check(SQLiteProvider.HasBeenRegistered && MySQLProvider.HasBeenRegistered && MariaDBProvider.HasBeenRegistered,
            "Provider flags must reflect complete central registrations.");
    }

    private static void CheckCompleteRegistrations()
    {
        foreach (var (type, registration) in PluginHook.Registrations)
        {
            Check(ReferenceEquals(PluginHook.DatabaseProviders[type], registration.DatabaseProvider), "Partial provider publication.");
            Check(ReferenceEquals(PluginHook.SqlFromMetadataFactories[type], registration.SqlFromMetadataFactory), "Partial SQL factory publication.");
            Check(ReferenceEquals(PluginHook.MetadataFromSqlFactories[type], registration.MetadataFromSqlFactory), "Partial metadata publication.");
        }
    }

    private static void CheckSnapshotsAndReplacement()
    {
        var before = PluginHook.Registrations;
        var beforeProviders = PluginHook.DatabaseProviders;
        var replacementCreator = new SQLiteDatabaseCreator();
        var replacementSql = new SqlFromSQLiteFactory();
        var replacementMetadata = new MetadataFromSQLiteFactoryCreator();
        Check(!PluginHook.RegisterProvider(DatabaseType.SQLite, replacementCreator, replacementSql, replacementMetadata),
            "Repeated registration must retain the winner.");
        Check(ReferenceEquals(before, PluginHook.Registrations), "Rejected registration replaced the snapshot.");
        Check(PluginHook.RegisterProvider(DatabaseType.SQLite, replacementCreator, replacementSql, replacementMetadata, replaceExisting: true),
            "Explicit replacement failed.");
        Check(ReferenceEquals(beforeProviders[DatabaseType.SQLite], before[DatabaseType.SQLite].DatabaseProvider), "Old snapshot changed.");
        Check(!ReferenceEquals(before[DatabaseType.SQLite].DatabaseProvider, replacementCreator), "Old registration changed.");
        CheckCompleteRegistrations();
        Check(ReferenceEquals(PluginHook.Registrations[DatabaseType.SQLite].DatabaseProvider, replacementCreator), "Replacement was not published.");
        try
        {
            ((IDictionary<DatabaseType, IDatabaseProviderCreator>)PluginHook.DatabaseProviders).Clear();
            throw new InvalidOperationException("Published snapshots must reject mutation.");
        }
        catch (NotSupportedException)
        {
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
