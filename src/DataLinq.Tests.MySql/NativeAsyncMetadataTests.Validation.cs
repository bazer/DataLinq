using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.MySql;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using DataLinq.Validation;
using MySqlConnector;

namespace DataLinq.Tests.MySql;

public sealed partial class NativeAsyncMetadataTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task PublicValidation_CompiledServerModelRoundtripAndScopedDrift(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(PublicValidation_CompiledServerModelRoundtripAndScopedDrift));
        using var provider = Provider(descriptor, schema.Connection.ConnectionString, schema.Connection.DataSourceName);
        schema.ExecuteNonQuery(provider.GetCreateSql().Text);
        var database = new ValidationDatabase(provider);
        var sync = database.ValidateSchema();
        var asyncResult = await DataLinqSchemaValidator.ValidateAsync(provider);
        if (sync.Differences.Count != 0) Console.WriteLine(string.Join("\n", sync.Differences.Select(item => item.Message)));
        await Assert.That(sync.Differences).IsEmpty();
        await Assert.That(sync.Issues).IsEmpty();
        await Assert.That(sync.HasFailures).IsFalse();
        await Assert.That(asyncResult.Differences).IsEmpty();
        await Assert.That(sync.DatabaseName).IsEqualTo(schema.Connection.DataSourceName);
        database.EnsureSchemaValid();
        await database.EnsureSchemaValidAsync();
        schema.ExecuteNonQuery("ALTER TABLE employees DROP COLUMN first_name");
        var drift = database.ValidateSchema(options => options.Include = ["employees"]);
        await Assert.That(drift.ModelTableCount).IsEqualTo(1);
        await Assert.That(drift.DatabaseTableCount).IsEqualTo(1);
        await Assert.That(drift.Differences.Single().Kind).IsEqualTo(SchemaDifferenceKind.MissingColumn);
        await Assert.That(drift.Differences.Single().Path).IsEqualTo("employees.first_name");
        await Assert.That(() => database.EnsureSchemaValid(options => options.Include = ["employees"])).Throws<DataLinqSchemaValidationException>();
        await Assert.That(() => database.EnsureSchemaValidAsync(options => options.Include = ["employees"])).Throws<DataLinqSchemaValidationException>();
        await Assert.That(sync.Differences).IsEmpty();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task PublicValidation_ServerOperationalAndCallbackFailuresKeepOriginalEvidence(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(PublicValidation_ServerOperationalAndCallbackFailuresKeepOriginalEvidence), Schema);
        using var provider = Provider(descriptor, schema.Connection.ConnectionString, schema.Connection.DataSourceName);
        var original = new InvalidOperationException("original callback Server=example;Password=literal-test-text; 'A' differs from 'a'");
        var sync = await Assert.That(() => DataLinqSchemaValidator.Validate(provider, new() { MetadataReaderLog = _ => throw original })).Throws<InvalidOperationException>();
        await Assert.That(sync).IsSameReferenceAs(original);
        var asynchronous = await Assert.That(async () => { await DataLinqSchemaValidator.ValidateAsync(provider, new() { MetadataReaderLog = _ => throw original }); }).Throws<InvalidOperationException>();
        await Assert.That(asynchronous).IsSameReferenceAs(original);
        var completed = DataLinqSchemaValidator.Validate(provider);
        await Assert.That(completed.Issues.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(provider.TableExists("order-header")).IsTrue();
        var invalid = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString) { UserID = "w5_missing_user", Password = "invalid" };
        using var denied = Provider(descriptor, invalid.ConnectionString, schema.Connection.DataSourceName);
        await Assert.That(() => DataLinqSchemaValidator.Validate(denied)).Throws<MySqlException>();
        await Assert.That(async () => { await DataLinqSchemaValidator.ValidateAsync(denied); }).Throws<MySqlException>();
    }

    private sealed class ValidationDatabase(SqlProvider<EmployeesDb> provider) : Database<EmployeesDb>(provider);
}
