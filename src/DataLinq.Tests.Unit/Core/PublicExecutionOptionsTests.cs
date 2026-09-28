using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Logging;
using DataLinq.MariaDB;
using DataLinq.MySql;
using DataLinq.SQLite;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Unit.Core;

public sealed class PublicExecutionOptionsTests
{
    [Test]
    [Arguments(-1d)]
    [Arguments(0d)]
    [Arguments(0.5d)]
    [Arguments(4294967295d)]
    public async Task InvalidOptionsAreRejectedBeforeNativeProviderSetup(double milliseconds)
    {
        var options = new DataLinqExecutionOptions { RecoveryRollbackTimeout = TimeSpan.FromMilliseconds(milliseconds) };
        // Deliberately invalid connection strings prove options validation precedes parsing/setup.
        await Assert.That(() => new SQLiteProvider<EmployeesDb>("invalid", null, null, options)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new MySqlProvider<EmployeesDb>("invalid", null, null, options)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new MariaDBProvider<EmployeesDb>("invalid", null, null, options)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task NullOptionsAreRejectedBeforeProviderSetup()
    {
        await Assert.That(() => new SQLiteProvider<EmployeesDb>("invalid", null, null, null!)).Throws<ArgumentNullException>();
        await Assert.That(() => new MySqlProvider<EmployeesDb>("invalid", null, null, null!)).Throws<ArgumentNullException>();
        await Assert.That(() => new MariaDBProvider<EmployeesDb>("invalid", null, null, null!)).Throws<ArgumentNullException>();
    }

    [Test]
    [Arguments(1d)]
    [Arguments(4294967294d)]
    public async Task AcceptedBoundsAreCapturedSeparatelyAndPassedToTransactions(double milliseconds)
    {
        var options = new DataLinqExecutionOptions { RecoveryRollbackTimeout = TimeSpan.FromMilliseconds(milliseconds) };
        await using var provider = new SQLiteProvider<EmployeesDb>("Data Source=:memory:", null, null, options);
        await Assert.That(ReferenceEquals(provider.ExecutionOptions, options)).IsFalse();
        await Assert.That(provider.ExecutionOptions.RecoveryRollbackTimeout).IsEqualTo(options.RecoveryRollbackTimeout);
        await using var transaction = provider.StartTransaction();
        await Assert.That(transaction.RecoverySettings.RecoveryRollbackTimeout).IsEqualTo(options.RecoveryRollbackTimeout);
    }

    [Test]
    public async Task ExistingConstructorBindingsKeepDefaultsAndNewOptionsAreRequired()
    {
        await using var provider = new SQLiteProvider<EmployeesDb>("Data Source=:memory:", loggerFactory: null);
        await Assert.That(provider.ExecutionOptions.RecoveryRollbackTimeout).IsEqualTo(TimeSpan.FromSeconds(30));
        foreach (var type in new[] { typeof(SQLiteProvider<EmployeesDb>), typeof(MySqlProvider<EmployeesDb>), typeof(MariaDBProvider<EmployeesDb>) })
        {
            var added = type.GetConstructors().Single(ctor => ctor.GetParameters().Last().ParameterType == typeof(DataLinqExecutionOptions));
            await Assert.That(added.GetParameters().Select(p => p.Name!).ToArray()).IsEquivalentTo(new[] { "connectionString", "databaseName", "loggerFactory", "executionOptions" });
            await Assert.That(added.GetParameters().All(p => !p.IsOptional)).IsTrue();
            await Assert.That(type.GetConstructor([typeof(string), typeof(string), typeof(DataLinqLoggingConfiguration)]) is not null).IsTrue();
        }
    }
}
