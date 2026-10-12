using W4HostConsumer;

await GenericHostScenario.Run(asynchronousDisposal: false);
await GenericHostScenario.Run(asynchronousDisposal: true);
#if NET10_0
await WebHostScenario.Run();
#endif
Console.WriteLine($"W4 host consumer passed on .NET {Environment.Version}.");
