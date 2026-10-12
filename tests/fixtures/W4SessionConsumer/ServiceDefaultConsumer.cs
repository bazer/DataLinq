using DataLinq.Extensions.DependencyInjection;
using DataLinq.Interfaces;
using DataLinq.SQLite.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace SessionConsumer;

internal static class ServiceDefaultConsumer
{
    internal static async Task Run()
    {
        foreach (var registered in new[] { false, true })
        {
            var services = new ServiceCollection();
            services.AddDataLinq<TestDb>().UseSQLite("Data Source=:memory:")
                .AddSingletonService<DefaultService>().AddScopedService<TraditionalDefaultService>();
            if (registered)
            {
                services.AddSingleton(typeof(byte), (byte)9);
                services.AddSingleton(typeof(short), (short)11);
                services.AddSingleton(typeof(long), 13L);
                services.AddSingleton(typeof(float), 3.5f);
                services.AddSingleton(typeof(decimal), 7.5m);
                services.AddKeyedSingleton(typeof(float), "scale", 9.5f);
                services.AddKeyedSingleton(typeof(byte), "retries", (byte)15);
            }
            await using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            await using var scope = container.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var factory = provider.GetRequiredService<IDataLinqUnitOfWorkFactory<TestDb>>();
            var generated = provider.GetRequiredService<DefaultService>();
            var authored = registered ? new DefaultService(factory, 9, 11, 13L, 3.5f, 7.5m, keyedScale: 9.5f) : new DefaultService(factory);
            if (!generated.Defaults.SequenceEqual(authored.Defaults)) throw new Exception("DI defaults differ from an authored constructor call.");
            var traditional = provider.GetRequiredService<TraditionalDefaultService>();
            if (traditional.Retries != (registered ? 15 : 3) || traditional.ReadRetries() != traditional.Retries)
                throw new Exception("Traditional keyed constructor default/override was not preserved.");
            if (generated.Scale() != 1.5f || generated.Price() != 1.2345678901234567890123456789m || generated.Day() != DayOfWeek.Monday ||
                !double.IsNaN(generated.NotANumber()) || !float.IsNegativeInfinity(generated.Infinity()) ||
                BitConverter.DoubleToInt64Bits(generated.NegativeZero()) != long.MinValue ||
                generated.Escaped() != "quote\"slash\\line\n" || generated.Character() != '\n' || await generated.ScaleAsync() != 1.5f)
                throw new Exception("Owning operation defaults changed their authored values.");
            using var session = factory.Create();
            if (generated.Scale(session) != 1.5f || generated.Price(session) != 1.2345678901234567890123456789m || generated.Day(session) != DayOfWeek.Monday ||
                !double.IsNaN(generated.NotANumber(session)) || !float.IsNegativeInfinity(generated.Infinity(session)) ||
                BitConverter.DoubleToInt64Bits(generated.NegativeZero(session)) != long.MinValue ||
                generated.Escaped(session) != "quote\"slash\\line\n" || generated.Character(session) != '\n' || await generated.ScaleAsync(session) != 1.5f)
                throw new Exception("Participating operation defaults changed their authored values.");
        }
    }
}
