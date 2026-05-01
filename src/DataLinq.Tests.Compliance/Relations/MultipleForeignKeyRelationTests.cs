using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class MultipleForeignKeyRelationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_MultipleForeignKeysToSameTable_LazyLoadsDistinctRelations(TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(
            provider,
            nameof(Transaction_MultipleForeignKeysToSameTable_LazyLoadsDistinctRelations));

        using var transaction = databaseScope.Database.Transaction();

        transaction.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO runtime_accounts (id, name) VALUES (1, 'Creator')");
        transaction.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO runtime_accounts (id, name) VALUES (2, 'Approver')");
        transaction.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO runtime_invoices (id, created_by_account_id, approved_by_account_id, number) VALUES (100, 1, 2, 'INV-100')");

        var invoice = transaction.Query().Invoices.Single(x => x.Id == 100);
        var createdBy = invoice.CreatedByAccount;
        var approvedBy = invoice.ApprovedByAccount;
        var creator = transaction.Query().Accounts.Single(x => x.Id == 1);
        var approver = transaction.Query().Accounts.Single(x => x.Id == 2);

        await Assert.That(createdBy.Name).IsEqualTo("Creator");
        await Assert.That(approvedBy.Name).IsEqualTo("Approver");
        await Assert.That(ReferenceEquals(createdBy, creator)).IsTrue();
        await Assert.That(ReferenceEquals(approvedBy, approver)).IsTrue();

        var createdInvoices = creator.CreatedInvoices.ToArray();
        var approvedInvoices = approver.ApprovedInvoices.ToArray();

        await Assert.That(createdInvoices.Length).IsEqualTo(1);
        await Assert.That(approvedInvoices.Length).IsEqualTo(1);
        await Assert.That(ReferenceEquals(invoice, createdInvoices.Single())).IsTrue();
        await Assert.That(ReferenceEquals(invoice, approvedInvoices.Single())).IsTrue();
        await Assert.That(creator.ApprovedInvoices).IsEmpty();
        await Assert.That(approver.CreatedInvoices).IsEmpty();

        transaction.Commit();
    }
}

[Database("multiple_fk_relation")]
public sealed partial class MultipleForeignKeyRelationDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<RuntimeAccount> Accounts { get; } = new(dataSource);
    public DbRead<RuntimeInvoice> Invoices { get; } = new(dataSource);
}

[Table("runtime_accounts")]
public abstract partial class RuntimeAccount(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<RuntimeAccount, MultipleForeignKeyRelationDb>(rowData, dataSource), ITableModel<MultipleForeignKeyRelationDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("id")]
    public abstract int Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 64)]
    [Type(DatabaseType.MariaDB, "varchar", 64)]
    [Column("name")]
    public abstract string Name { get; }

    [Relation("runtime_invoices", "created_by_account_id", "FK_runtime_invoice_created_by")]
    public abstract IImmutableRelation<RuntimeInvoice> CreatedInvoices { get; }

    [Relation("runtime_invoices", "approved_by_account_id", "FK_runtime_invoice_approved_by")]
    public abstract IImmutableRelation<RuntimeInvoice> ApprovedInvoices { get; }
}

[Table("runtime_invoices")]
public abstract partial class RuntimeInvoice(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<RuntimeInvoice, MultipleForeignKeyRelationDb>(rowData, dataSource), ITableModel<MultipleForeignKeyRelationDb>
{
    [PrimaryKey]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("id")]
    public abstract int Id { get; }

    [ForeignKey("runtime_accounts", "id", "FK_runtime_invoice_created_by")]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("created_by_account_id")]
    public abstract int CreatedByAccountId { get; }

    [ForeignKey("runtime_accounts", "id", "FK_runtime_invoice_approved_by")]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("approved_by_account_id")]
    public abstract int ApprovedByAccountId { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Type(DatabaseType.MySQL, "varchar", 32)]
    [Type(DatabaseType.MariaDB, "varchar", 32)]
    [Column("number")]
    public abstract string Number { get; }

    [Relation("runtime_accounts", "id", "FK_runtime_invoice_created_by")]
    public abstract RuntimeAccount CreatedByAccount { get; }

    [Relation("runtime_accounts", "id", "FK_runtime_invoice_approved_by")]
    public abstract RuntimeAccount ApprovedByAccount { get; }
}
