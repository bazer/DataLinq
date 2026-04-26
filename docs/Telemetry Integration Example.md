# Telemetry Integration Example

This page shows one sane way to hook DataLinq into normal .NET telemetry collection.

The core principle is simple:

- DataLinq emits telemetry
- your application collects and exports it

That means the DataLinq library itself should stay vendor-neutral, while the app decides whether to use OpenTelemetry, `dotnet-counters`, console exporters, or something else.

## Example: ASP.NET Core with OpenTelemetry

This is the normal production-style shape.

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("MyApp"))
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("DataLinq")
            .AddRuntimeInstrumentation()
            .AddAspNetCoreInstrumentation();

        // Add your exporter here when needed.
        // metrics.AddOtlpExporter();
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("DataLinq")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        // Add your exporter here when needed.
        // tracing.AddOtlpExporter();
    });

var app = builder.Build();
app.Run();
```

The two DataLinq-specific lines are the important ones:

- `AddMeter("DataLinq")`
- `AddSource("DataLinq")`

Without those, your app may be fully instrumented and still ignore DataLinq entirely.

## What You Get

With that wiring in place, DataLinq contributes:

- query count and query duration
- DB command count and command duration
- transaction start/completion count and duration
- mutation count, affected rows, and mutation duration
- cache occupancy gauges
- cache maintenance counters and duration
- query, command, transaction, and mutation spans

That is enough to answer real operational questions like:

- are we hitting the DB more than expected?
- are transactions getting slower?
- are mutations spiking?
- is the cache growing or cleaning up as expected?

## Local Debug Example Without OpenTelemetry

Sometimes you do not want a full exporter stack. You just want to prove that DataLinq is emitting the right signals.

This is a minimal in-process listener approach:

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

using var activityListener = new ActivityListener
{
    ShouldListenTo = source => source.Name == "DataLinq",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStopped = activity =>
    {
        Console.WriteLine($"[{activity.OperationName}] {activity.DisplayName}");
    }
};

ActivitySource.AddActivityListener(activityListener);

using var meterListener = new MeterListener();
meterListener.InstrumentPublished = (instrument, listener) =>
{
    if (instrument.Meter.Name == "DataLinq")
        listener.EnableMeasurementEvents(instrument);
};

meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
{
    Console.WriteLine($"{instrument.Name}: {measurement}");
});

meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
{
    Console.WriteLine($"{instrument.Name}: {measurement}");
});

meterListener.Start();
```

That is not a production pipeline. It is a local verification tool. But it is extremely useful when you want to confirm that a benchmark, test, or app path is emitting what you think it is emitting.

## Snapshot API vs Exported Telemetry

Use the standard .NET telemetry surface when you want:

- live observation across the whole app
- trace correlation with web requests or background jobs
- exporter/backend integration

Use `DataLinqMetrics.Snapshot()` when you want:

- provider/table drilldown
- deterministic before/after deltas
- benchmark and test assertions

These are not competing APIs. They solve different problems.

## Recommended First Workflow

If you are integrating this into a real app for the first time:

1. wire `AddMeter("DataLinq")` and `AddSource("DataLinq")`
2. confirm locally with `dotnet-counters` or a local listener
3. only then add your real exporter

That order is better than blindly wiring an exporter first and then guessing whether DataLinq is even included.
