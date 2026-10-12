using DataLinq;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Interfaces;
using DataLinq.SQLite;
using DataLinq.SQLite.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PackedGenerated;

internal static class ConfigurationConsumer
{
    internal static async Task Run()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:active"] = "Data Source=:memory:",
            ["Database:ConnectionString"] = "superseded literal",
            ["Database:Execution:RecoveryRollbackTimeout"] = "00:00:05"
        }).Build();
        var calls = 0;
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        var configured = services.AddDataLinq<AdvancedDatabase>("configured").UseSQLiteConnectionString("active")
            .Bind(config.GetSection("Database")).Configure(options =>
            {
                calls++;
                if (options.ConnectionString is not null || options.ConnectionStringName != "active")
                    throw new Exception("Named selection failed to replace bound literal.");
                options.DatabaseName = "w4_configured_" + Guid.NewGuid().ToString("N");
            }).AsDefault();
        services.AddDataLinq<AdvancedDatabase>("literal").UseSQLite("Data Source=:memory:");
        await using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        if (calls != 0) throw new Exception("Configuration ran eagerly.");
        var factory = container.GetRequiredService<IDataLinqUnitOfWorkFactory<AdvancedDatabase>>();
        var database = container.GetRequiredKeyedService<SQLiteDatabase<AdvancedDatabase>>("configured");
        config["ConnectionStrings:active"] = "invalid after resolution";
        if (calls != 1 || database.Provider.ExecutionOptions.RecoveryRollbackTimeout != TimeSpan.FromSeconds(5) ||
            !ReferenceEquals(database, container.GetRequiredService<Database<AdvancedDatabase>>()))
            throw new Exception("Configuration capture or identity changed.");
        factory.Execute(_ => { });
        await factory.ExecuteAsync(_ => Task.CompletedTask);
        var literal = container.GetRequiredKeyedService<SQLiteDatabase<AdvancedDatabase>>("literal");
        if (ReferenceEquals(literal, database) || literal.Provider.ExecutionOptions.RecoveryRollbackTimeout != TimeSpan.FromSeconds(30))
            throw new Exception("Per-key construction settings leaked.");
        if (!ReferenceEquals(configured.Registration.Services, services)) throw new Exception("Provider builder lost its registration.");
    }
}
