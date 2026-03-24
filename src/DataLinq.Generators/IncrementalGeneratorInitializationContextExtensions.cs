using System;
using Microsoft.CodeAnalysis;

namespace DataLinq.SourceGenerators;

internal static class IncrementalGeneratorInitializationContextExtensions
{
    private static readonly DiagnosticDescriptor UnhandledExceptionDescriptor = new(
        id: "DLG000",
        title: "DataLinq generator failure",
        messageFormat: "{0}",
        category: "DataLinq.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static void RegisterSourceOutputSafely<TSource>(
        this IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<TSource> source,
        Action<SourceProductionContext, TSource> action,
        string generatorName)
    {
        context.RegisterSourceOutput(source, (sourceProductionContext, value) =>
            ExecuteSafely(sourceProductionContext, value, action, generatorName));
    }

    public static void RegisterSourceOutputSafely<TSource>(
        this IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<TSource> source,
        Action<SourceProductionContext, TSource> action,
        string generatorName)
    {
        context.RegisterSourceOutput(source, (sourceProductionContext, value) =>
            ExecuteSafely(sourceProductionContext, value, action, generatorName));
    }

    public static void ReportInitializationException(
        this IncrementalGeneratorInitializationContext context,
        Exception exception,
        string generatorName)
    {
        context.RegisterSourceOutput(context.CompilationProvider, (sourceProductionContext, _) =>
            sourceProductionContext.ReportDiagnostic(CreateUnhandledExceptionDiagnostic(generatorName, exception)));
    }

    private static void ExecuteSafely<TSource>(
        SourceProductionContext context,
        TSource source,
        Action<SourceProductionContext, TSource> action,
        string generatorName)
    {
        try
        {
            action(context, source);
        }
        catch (Exception exception)
        {
            context.ReportDiagnostic(CreateUnhandledExceptionDiagnostic(generatorName, exception));
        }
    }

    private static Diagnostic CreateUnhandledExceptionDiagnostic(string generatorName, Exception exception)
    {
        var message = $"{generatorName} threw an unhandled exception:{'\n'}{exception}";

        return Diagnostic.Create(UnhandledExceptionDescriptor, Location.None, message);
    }
}
