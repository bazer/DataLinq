using DataLinq;
using DataLinq.Logging;
using DataLinq.MariaDB;
using DataLinq.MySql;
using DataLinq.PackageConsumer;
using DataLinq.SQLite;

namespace ExternalConstructorBindings;

// Linked into both old-baseline and candidate consumers. Compilation checks the
// same old positional/typed-null/untyped-null/named bindings against each package.
public static class Constructors
{
    public static Func<object>[] Bind(string connectionString) => [
        () => new MySqlProvider<PackageConsumerDatabase>(connectionString),
        () => new MySqlProvider<PackageConsumerDatabase>(connectionString, null),
        () => new MySqlProvider<PackageConsumerDatabase>(connectionString, (string?)null, (DataLinqLoggingConfiguration?)null),
        () => new MySqlProvider<PackageConsumerDatabase>(connectionString: connectionString, databaseName: null, loggerFactory: null),
        () => new MariaDBProvider<PackageConsumerDatabase>(connectionString),
        () => new MariaDBProvider<PackageConsumerDatabase>(connectionString, null),
        () => new MariaDBProvider<PackageConsumerDatabase>(connectionString: connectionString, databaseName: null, loggerFactory: null),
        () => new SQLiteProvider<PackageConsumerDatabase>(connectionString),
        () => new SQLiteProvider<PackageConsumerDatabase>(connectionString, null),
        () => new SQLiteProvider<PackageConsumerDatabase>(connectionString, (string?)null),
        () => new SQLiteProvider<PackageConsumerDatabase>(connectionString, (DataLinqLoggingConfiguration?)null),
        () => new SQLiteProvider<PackageConsumerDatabase>(connectionString: connectionString, loggerFactory: null),
        () => new SQLiteProvider<PackageConsumerDatabase>(connectionString: connectionString, databaseName: null, loggerFactory: null)
    ];

    public static string UntypedNullBinding()
    {
        System.Linq.Expressions.Expression<Func<SQLiteProvider<PackageConsumerDatabase>>> expression =
            () => new SQLiteProvider<PackageConsumerDatabase>("Data Source=:memory:", null);
        var creation = (System.Linq.Expressions.NewExpression)expression.Body;
        return creation.Constructor!.GetParameters()[1].ParameterType.FullName!;
    }

    public static DatabaseProvider CreateSql() => new DerivedSql("Server=localhost;Database=constructor_probe", null);

#if PACKED_ASYNC_CONSUMER
    public static Func<object>[] OptionsBindings(string connectionString, DataLinqExecutionOptions executionOptions) => [
        () => new MySqlProvider<PackageConsumerDatabase>(connectionString, null, null, executionOptions),
        () => new MariaDBProvider<PackageConsumerDatabase>(connectionString: connectionString, databaseName: null, loggerFactory: null, executionOptions: executionOptions),
        () => new SQLiteProvider<PackageConsumerDatabase>(connectionString: connectionString, databaseName: null, loggerFactory: null, executionOptions: executionOptions)
    ];
#endif

    // SqlProvider construction does not open a server connection. Do not execute
    // the MariaDB delegates above: its eager server-version probe is a separate boundary.
    private sealed class DerivedSql : SqlProvider<PackageConsumerDatabase>
    {
        public DerivedSql(string connectionString) : base(connectionString, DatabaseType.MySQL, DataLinqLoggingConfiguration.NullConfiguration) { }
        public DerivedSql(string connectionString, string? databaseName)
            : base(connectionString, DatabaseType.MySQL, DataLinqLoggingConfiguration.NullConfiguration, databaseName) { }
#if PACKED_ASYNC_CONSUMER
        public DerivedSql(string connectionString, string? databaseName, DataLinqExecutionOptions executionOptions)
            : base(connectionString, DatabaseType.MySQL, DataLinqLoggingConfiguration.NullConfiguration, databaseName, executionOptions) { }
#endif
    }

    public abstract class DerivedGeneric : DatabaseProvider<PackageConsumerDatabase>
    {
        protected DerivedGeneric(string connectionString)
            : base(connectionString, DatabaseType.SQLite, DataLinqLoggingConfiguration.NullConfiguration) { }
        protected DerivedGeneric(string connectionString, string? databaseName)
            : base(connectionString, DatabaseType.SQLite, DataLinqLoggingConfiguration.NullConfiguration, databaseName) { }
#if PACKED_ASYNC_CONSUMER
        protected DerivedGeneric(string connectionString, string? databaseName, DataLinqExecutionOptions executionOptions)
            : base(connectionString, DatabaseType.SQLite, DataLinqLoggingConfiguration.NullConfiguration, databaseName, executionOptions) { }
#endif
    }

    public abstract class DerivedNonGeneric : DatabaseProvider
    {
        protected DerivedNonGeneric(string connectionString)
            : base(connectionString, typeof(PackageConsumerDatabase), DatabaseType.SQLite, DataLinqLoggingConfiguration.NullConfiguration) { }
        protected DerivedNonGeneric(string connectionString, string? databaseName)
            : base(connectionString, typeof(PackageConsumerDatabase), DatabaseType.SQLite, DataLinqLoggingConfiguration.NullConfiguration,
                databaseName, metadataFactory: null, createReadOnlyAccess: false) { }
#if PACKED_ASYNC_CONSUMER
        protected DerivedNonGeneric(string connectionString, string? databaseName, DataLinqExecutionOptions executionOptions)
            : base(connectionString, typeof(PackageConsumerDatabase), DatabaseType.SQLite, DataLinqLoggingConfiguration.NullConfiguration,
                databaseName, metadataFactory: null, createReadOnlyAccess: false, executionOptions: executionOptions) { }
#endif
    }
}
