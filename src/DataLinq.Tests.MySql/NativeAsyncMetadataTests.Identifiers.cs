using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Execution;
using DataLinq.Testing;
using DataLinq.Validation;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public sealed partial class NativeAsyncMetadataTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task NativeIdentifiers_ObservesFreshRulesAndPreservesNativeCaseAndUnicode(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(NativeIdentifiers_ObservesFreshRulesAndPreservesNativeCaseAndUnicode),
            "CREATE TABLE `ÅParent` (`ID` INT PRIMARY KEY, `Value` VARCHAR(40) NOT NULL DEFAULT 'A' COMMENT 'A', UNIQUE INDEX `IX_Value` (`value`))",
            "CREATE TABLE `Child` (`ID` INT PRIMARY KEY, `ParentID` INT NOT NULL, CONSTRAINT `FK_Parent` FOREIGN KEY (`parentid`) REFERENCES `ÅParent` (`id`))");
        using var provider = Provider(descriptor, schema.Connection.ConnectionString, schema.Connection.DataSourceName);
        var settings = MetadataReadSettings.Runtime(null, null);
        var first = provider.ReadValidationMetadataCore(settings).ValueOrException();
        var rules = settings.Identifiers!;
        Console.WriteLine($"Observed {descriptor.DatabaseType} @@lower_case_table_names={rules.LowerCaseTableNames}.");
        await Assert.That(rules.DatabaseType).IsEqualTo(descriptor.DatabaseType);
        await Assert.That(rules.LowerCaseTableNames is 0 or 1 or 2).IsTrue();
        if (rules.LowerCaseTableNames == 0)
        {
            schema.ExecuteNonQuery("CREATE TABLE `åparent` (`other` INT PRIMARY KEY)");
            schema.ExecuteNonQuery("CREATE TABLE `Orders` (`upper_only` INT PRIMARY KEY); CREATE TABLE `orders` (`lower_only` INT PRIMARY KEY)");
            if (descriptor.DatabaseType == DatabaseType.MariaDB)
            {
                schema.ExecuteNonQuery("ALTER TABLE `Orders` ADD CONSTRAINT `ck_shared` CHECK (`upper_only` > 0)");
                schema.ExecuteNonQuery("ALTER TABLE `orders` ADD CONSTRAINT `ck_shared` CHECK (`lower_only` > 1)");
            }
        }
        var syncSettings = MetadataReadSettings.Runtime(TimeSpan.FromSeconds(2), null);
        var sync = provider.ReadValidationMetadataCore(syncSettings).ValueOrException();
        var asyncSettings = MetadataReadSettings.Runtime(TimeSpan.FromSeconds(2), null);
        var asyncResult = (await provider.ReadValidationMetadataAsyncCore(asyncSettings)).ValueOrException();
        await Assert.That(asyncSettings.Identifiers!.LowerCaseTableNames).IsEqualTo(rules.LowerCaseTableNames);
        await Assert.That(asyncSettings.Identifiers).IsNotSameReferenceAs(syncSettings.Identifiers);
        await Assert.That(MetadataEquivalenceDigest.CreateText(sync)).IsEqualTo(MetadataEquivalenceDigest.CreateText(asyncResult));
        if (rules.LowerCaseTableNames == 0)
        {
            await Assert.That(sync.TableModels.Length).IsEqualTo(5);
            await Assert.That(sync.TableModels.Single(table => table.Table.DbName == "Orders").Table.Columns.Single().DbName).IsEqualTo("upper_only");
            await Assert.That(sync.TableModels.Single(table => table.Table.DbName == "orders").Table.Columns.Single().DbName).IsEqualTo("lower_only");
            if (descriptor.DatabaseType == DatabaseType.MariaDB)
            {
                await Assert.That(sync.TableModels.Single(table => table.Table.DbName == "Orders").Model.Attributes.OfType<CheckAttribute>().Single().Expression).Contains("upper_only");
                await Assert.That(sync.TableModels.Single(table => table.Table.DbName == "orders").Model.Attributes.OfType<CheckAttribute>().Single().Expression).Contains("lower_only");
            }
        }
        var childName = rules.LowerCaseTableNames == 0 ? "Child" : sync.TableModels.Single(table => rules.TableNames.Equals(table.Table.DbName, "Child")).Table.DbName;
        var selected = SchemaComparisonScope.Capture(first, descriptor.DatabaseType, [childName]).Resolve(sync, rules);
        await Assert.That(selected.Model.Count).IsEqualTo(1);
        await Assert.That(selected.Database.Count).IsEqualTo(1);
        await Assert.That(new SchemaComparer(rules).CompareRuntime(selected.Model, selected.Database)).IsEmpty();
        var relations = selected.Database[0].Table.ColumnIndices.SelectMany(index => index.RelationParts).Select(part => part.Relation).Distinct().ToArray();
        await Assert.That(relations.Length).IsEqualTo(1);
        await Assert.That(relations[0].CandidateKey.ColumnIndex.Columns.Single().DbName).IsEqualTo("ID");
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task NativeIdentifiers_InvalidSettingSettlesBeforeAnyCatalogQuery(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(NativeIdentifiers_InvalidSettingSettlesBeforeAnyCatalogQuery), "CREATE TABLE observed(id INT PRIMARY KEY)");
        using var provider = Provider(descriptor, schema.Connection.ConnectionString, schema.Connection.DataSourceName);
        foreach (var asynchronous in new[] { false, true })
        {
            var settings = MetadataReadSettings.Runtime(TimeSpan.FromMilliseconds(1500), null);
            var failures = new ExecutionFailures();
            Exception? error = null;
            if (asynchronous)
            {
                var plan = ((IAsyncProviderMetadataSource)provider).CaptureValidationMetadata(settings);
                var session = plan.CreateSession();
                var access = new InvalidSettingAccess(session.Access);
                var context = new MetadataReadContext(access, session.Commands, settings.CommandTimeoutSeconds, default);
                try
                {
                    await session.OpenAsync(default);
                    error = await Assert.That(async () => { await plan.ReadAsync(context, default); }).Throws<InvalidOperationException>();
                }
                finally { await context.CloseAsync(failures); await session.DisposeAsync(); }
                await Assert.That(access.Commands).IsEqualTo(1);
            }
            else
            {
                var plan = ((ISyncProviderMetadataSource)provider).CaptureValidationMetadata(settings);
                var session = new InvalidSettingSession(plan.CreateSession());
                var context = new SyncMetadataReadContext(session, settings.CommandTimeoutSeconds, provider.TelemetryInstanceId);
                try
                {
                    session.Open();
                    error = await Assert.That(() => plan.Read(context)).Throws<InvalidOperationException>();
                }
                finally { context.Close(failures); session.Dispose(); }
                await Assert.That(session.Commands).IsEqualTo(1);
            }
            await Assert.That(error!.Message).Contains("@@lower_case_table_names");
            await Assert.That(settings.Identifiers).IsNull();
            // Reuse the one-slot native pool only after the failed read settles.
            await Assert.That(provider.ReadValidationMetadataCore().ValueOrException().TableModels.Length).IsEqualTo(1);
        }
    }

    private static bool IsSetting(IDbCommand command) => command.CommandText == "SELECT @@lower_case_table_names";

    // Exercise the invalid interpretation branch with a real native command and
    // real ownership. This is controlled branch coverage, not a server mode.
    private sealed class InvalidSettingAccess(IAsyncDatabaseAccess inner) : IAsyncDatabaseAccess
    {
        internal int Commands;
        public void ValidateCommand(IDbCommand command, AsyncCommandKind kind) => inner.ValidateCommand(command, kind);
        public void ValidateReader(IDbCommand command) => inner.ValidateReader(command);
        public Task<IAsyncDataReader> ExecuteReaderAsync(IDbCommand command, CancellationToken token) => throw new InvalidOperationException("Catalog must not run after an invalid setting.");
        public async Task<object?> ExecuteScalarAsync(IDbCommand command, CancellationToken token)
        {
            if (!IsSetting(command) || command.CommandTimeout != 2) throw new InvalidOperationException("Unexpected setting command or timeout.");
            Commands++;
            _ = await inner.ExecuteScalarAsync(command, token);
            return 99;
        }
        public Task<int> ExecuteNonQueryAsync(IDbCommand command, CancellationToken token) => throw new InvalidOperationException("Metadata must remain observational.");
    }

    private sealed class InvalidSettingSession(ISyncMetadataSession inner) : ISyncMetadataSession
    {
        internal int Commands;
        public void Validate(int? timeout) => inner.Validate(timeout);
        public void Open() => inner.Open();
        public IDbCommand CreateCommand(CapturedSql sql) => inner.CreateCommand(sql);
        public IDataLinqDataReader ExecuteReader(IDbCommand command) => throw new InvalidOperationException("Catalog must not run after an invalid setting.");
        public object? ExecuteScalar(IDbCommand command)
        {
            if (!IsSetting(command) || command.CommandTimeout != 2) throw new InvalidOperationException("Unexpected setting command or timeout.");
            Commands++;
            _ = inner.ExecuteScalar(command);
            return 99;
        }
        public void Dispose() => inner.Dispose();
    }
}
