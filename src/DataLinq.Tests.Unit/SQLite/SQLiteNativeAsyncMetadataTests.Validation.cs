using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Metadata;
using DataLinq.SQLite;
using DataLinq.Tests.Models.Employees;
using DataLinq.Validation;
using Microsoft.Data.Sqlite;

namespace DataLinq.Tests.Unit.SQLite;

public sealed partial class SQLiteNativeAsyncMetadataTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PublicValidation_CompiledModelRoundtripAndScopedDrift(bool memory)
    {
        using var fixture = new Fixture(memory);
        fixture.Provider.DatabaseAccess.ExecuteNonQuery(fixture.Provider.GetCreateSql().Text);
        var database = new ValidationDatabase(fixture.Provider);
        var sync = database.ValidateSchema();
        var asyncResult = await DataLinqSchemaValidator.ValidateAsync(fixture.Provider);
        if (sync.Differences.Count != 0) Console.WriteLine(string.Join("\n", sync.Differences.Select(item => item.Message)));
        await Assert.That(string.Join("\n", sync.Differences.Select(item => item.Message))).IsEqualTo("");
        await Assert.That(sync.Issues.Count).IsEqualTo(fixture.Provider.Metadata.TableModels.Where(table => table.Table.Type == TableType.View).Sum(table => table.Table.Columns.Length));
        await Assert.That(sync.HasFailures).IsTrue();
        await Assert.That(asyncResult.Differences).IsEmpty();
        await Assert.That(sync.DatabaseName).IsEqualTo(fixture.Name);
        await Assert.That(sync.ModelTableCount).IsEqualTo(sync.DatabaseTableCount);
        database.EnsureSchemaValid(options => options.TreatValidationIssuesAsFailures = false);
        await database.EnsureSchemaValidAsync(options => options.TreatValidationIssuesAsFailures = false);
        var tables = fixture.Provider.Metadata.TableModels.Where(table => table.Table.Type == TableType.Table).Select(table => table.Table.DbName).ToArray();
        var tablesOnly = database.ValidateSchema(options => options.Include = tables);
        await Assert.That(tablesOnly.HasFailures).IsFalse();
        await Assert.That(tablesOnly.Issues).IsEmpty();

        var name = tables[0];
        // Drop dependent views first: SQLite cannot read an invalid view, and
        // Include intentionally does not suppress operational catalog failures.
        foreach (var view in fixture.Provider.Metadata.TableModels.Where(table => table.Table.Type == TableType.View))
            fixture.Provider.DatabaseAccess.ExecuteNonQuery("DROP VIEW \"" + view.Table.DbName.Replace("\"", "\"\"") + "\"");
        fixture.Provider.DatabaseAccess.ExecuteNonQuery("PRAGMA foreign_keys=OFF; DROP TABLE \"" + name.Replace("\"", "\"\"") + "\"");
        var drift = database.ValidateSchema(options => options.Include = [name.ToUpperInvariant()]);
        await Assert.That(drift.ModelTableCount).IsEqualTo(1);
        await Assert.That(drift.DatabaseTableCount).IsEqualTo(0);
        await Assert.That(drift.Differences.Single().Kind).IsEqualTo(SchemaDifferenceKind.MissingTable);
        var rejected = await Assert.That(() => database.EnsureSchemaValid(options => options.Include = [name])).Throws<DataLinqSchemaValidationException>();
        await Assert.That(rejected!.Result.HasFailures).IsTrue();
        var rejectedAsync = await Assert.That(() => database.EnsureSchemaValidAsync(options => options.Include = [name])).Throws<DataLinqSchemaValidationException>();
        await Assert.That(rejectedAsync!.Result.DatabaseTableCount).IsEqualTo(0);
        await Assert.That(sync.Differences).IsEmpty();
    }

    [Test]
    public async Task PublicValidation_ConfigurationAndOperationalErrorsDoNotBecomeResults()
    {
        using var fixture = new Fixture(true, "CREATE TABLE sample(id INTEGER PRIMARY KEY, hidden INTEGER GENERATED ALWAYS AS(id + 1) STORED)");
        var database = new ValidationDatabase(fixture.Provider);
        var callbackFailure = new InvalidOperationException("original callback details 'A' => 'a'");
        var configured = 0;
        var configurationError = await Assert.That(() => { _ = database.ValidateSchemaAsync(options => { configured++; throw callbackFailure; }); })
            .Throws<InvalidOperationException>();
        await Assert.That(configurationError).IsSameReferenceAs(callbackFailure);
        await Assert.That(configured).IsEqualTo(1);
        await Assert.That(() => { _ = database.ValidateSchemaAsync(options => options.Include = ["absent-model-name"]); })
            .Throws<ArgumentException>();
        await Assert.That(async () => { await database.ValidateSchemaAsync(cancellationToken: new(true)); }).Throws<OperationCanceledException>();
        var invocation = SchemaValidationInvocation.Capture(fixture.Provider, CapturedSchemaValidationOptions.Capture(new()
        { MetadataReaderLog = _ => throw callbackFailure }));
        var syncError = await Assert.That(() => invocation.Execute()).Throws<InvalidOperationException>();
        await Assert.That(syncError).IsSameReferenceAs(callbackFailure);
        await Assert.That(invocation.CallbackFailure).IsSameReferenceAs(callbackFailure);
        var asyncError = await Assert.That(async () => { await database.ValidateSchemaAsync(options => options.MetadataReaderLog = _ => throw callbackFailure); })
            .Throws<InvalidOperationException>();
        await Assert.That(asyncError).IsSameReferenceAs(callbackFailure);
        var result = database.ValidateSchema();
        await Assert.That(result.Issues.Count).IsEqualTo(1);
        await Assert.That(result.HasFailures).IsTrue();
        await Assert.That(fixture.Provider.TableExists("sample")).IsTrue();

        using var missing = new Fixture(false);
        missing.ClearPools();
        File.Delete(missing.Path!);
        await Assert.That(() => DataLinqSchemaValidator.Validate(missing.Provider)).Throws<SqliteException>();
        await Assert.That(async () => { await DataLinqSchemaValidator.ValidateAsync(missing.Provider); }).Throws<SqliteException>();
    }

    private sealed class ValidationDatabase(SQLiteProvider<EmployeesDb> provider) : Database<EmployeesDb>(provider);
}
