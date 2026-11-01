using DataLinq;
using DataLinq.Exceptions;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Extensions.Hosting;
using DataLinq.Interfaces;
using DataLinq.MySql.Extensions.DependencyInjection;
using DataLinq.MariaDB.Extensions.DependencyInjection;
using DataLinq.SQLite;
using DataLinq.SQLite.Extensions.DependencyInjection;
using DataLinq.Validation;
using Microsoft.Extensions.DependencyInjection;
using SessionConsumer;

namespace W5Consumer;

internal static class Bindings
{
    internal static void Direct(Database<TestDb> database, CancellationToken token)
    {
        Func<Action<DataLinqSchemaValidationOptions>?, DataLinqSchemaValidationResult> validate = database.ValidateSchema;
        Action<Action<DataLinqSchemaValidationOptions>?> ensure = database.EnsureSchemaValid;
        Func<Action<DataLinqSchemaValidationOptions>?, CancellationToken, Task<DataLinqSchemaValidationResult>> validateAsync = database.ValidateSchemaAsync;
        Func<Action<DataLinqSchemaValidationOptions>?, CancellationToken, Task> ensureAsync = database.EnsureSchemaValidAsync;
        Func<IDatabaseProvider, DataLinqSchemaValidationOptions?, DataLinqSchemaValidationResult> staticValidate = DataLinqSchemaValidator.Validate;
        Func<IDatabaseProvider, DataLinqSchemaValidationOptions?, CancellationToken, Task<DataLinqSchemaValidationResult>> staticAsync = DataLinqSchemaValidator.ValidateAsync;
        Fixture.Check(!validate(null).HasFailures && !staticValidate(database.Provider, null).HasFailures, "Method-group binding failed.");
        ensure(null);
        // Compile-only expressions cover omitted, named, null and token-last forms.
        if (token.IsCancellationRequested)
        {
            _ = database.ValidateSchemaAsync();
            _ = database.ValidateSchemaAsync(null);
            _ = database.ValidateSchemaAsync(configure: null, cancellationToken: token);
            _ = validateAsync(null, token);
            _ = ensureAsync(null, token);
            _ = database.EnsureSchemaValidAsync(cancellationToken: token);
            _ = DataLinqSchemaValidator.ValidateAsync(database.Provider);
            _ = staticAsync(database.Provider, new(), token);
        }
    }

    internal static void Registration()
    {
        var services = new ServiceCollection();
        DataLinqRegistrationBuilder<TestDb> common = services.AddDataLinq<TestDb>("one").ValidateSchemaOnStartup();
        DataLinqProviderRegistrationBuilder<TestDb, SQLiteRegistrationOptions> typed = common.UseSQLite("Data Source=:memory:")
            .ValidateSchemaOnStartup().Configure(options => options.DatabaseName = "configured").AsDefault();
        DataLinqSchemaValidationBuilder central = services.AddDataLinqSchemaValidation(options => options.Mode = DataLinqSchemaStartupMode.Disabled)
            .ValidateDatabase<TestDb>()
            .ValidateDatabase<TestDb>(null)
            .ValidateDatabase<TestDb>("one")
            .ValidateDatabase<TestDb>(serviceKey: "one", configure: null)
            .ValidateDatabase<TestDb>(configure: options => options.Validation.Include = ["rows"]);
        Fixture.Check(ReferenceEquals(services, central.Services), "Builder lost service collection identity.");
        _ = typed;
        DataLinqProviderRegistrationBuilder<TestDb, MySqlRegistrationOptions> mysql = services.AddDataLinq<TestDb>("mysql")
            .UseMySql().ValidateSchemaOnStartup().Configure(options => options.ConnectionStringName = "MySql");
        DataLinqProviderRegistrationBuilder<TestDb, MariaDBRegistrationOptions> maria = services.AddDataLinq<TestDb>("maria")
            .UseMariaDB().ValidateSchemaOnStartup().Configure(options => options.ConnectionStringName = "MariaDB");
        _ = (mysql, maria);
    }

    internal static void Inspect(DataLinqSchemaStartupException failure)
    {
        foreach (DataLinqSchemaStartupOutcome outcome in failure.Outcomes)
        {
            DataLinqSchemaValidationTarget target = outcome.Target;
            Type model = target.ModelType;
            object? key = target.ServiceKey;
            int ordinal = target.Ordinal;
            string display = target.DisplayName;
            DataLinqSchemaStartupMode mode = outcome.Mode;
            DataLinqSchemaValidationResult? result = outcome.Result;
            Exception? operational = outcome.OperationalFailure;
            bool blocked = outcome.PreventsStartup;
            _ = (model, key, ordinal, display, mode, result, operational, blocked);
        }
        Exception? runFailure = failure.RunFailure;
        _ = runFailure;
    }

    internal static void InspectRead(DataLinqSchemaReadException failure)
    {
        DataLinq.ErrorHandling.IDLOptionFailure tree = failure.Failure;
        IReadOnlyList<Exception> originals = failure.Exceptions;
        _ = (tree, originals);
    }
}
