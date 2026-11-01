using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.ErrorHandling;
using DataLinq.Exceptions;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Interfaces;
using DataLinq.Validation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataLinq.Extensions.Hosting;

// Generic Host awaits every StartingAsync before invoking any StartAsync,
// including concurrent service starts. Constructors never resolve databases.
internal sealed class SchemaValidationHostedService(IServiceProvider services, SchemaValidationRegistration registration,
    ILogger<SchemaValidationHostedService> logger) : IHostedLifecycleService
{
    private readonly object sync = new();
    private Task? starting;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        lock (sync) return starting ??= ValidateAsync(cancellationToken);
    }

    private async Task ValidateAsync(CancellationToken cancellationToken)
    {
        var outcomes = new List<DataLinqSchemaStartupOutcome>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targets = registration.Capture(services);
            foreach (var target in targets)
            {
                if (target.Mode == DataLinqSchemaStartupMode.Disabled) continue;
                cancellationToken.ThrowIfCancellationRequested();
                IDatabaseProvider? provider = null;
                Exception? operational = null;
                using (var activation = DataLinqActivationFailureScope.Begin())
                {
                    try { provider = target.Registration.ResolveProvider(services); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception failure)
                    {
                        if (activation.ConfigurationFailure is not null) throw;
                        operational = failure;
                    }
                }
                DataLinqSchemaValidationResult? result = null;
                if (operational is null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Model Include/capability/lifecycle configuration can fail
                    // synchronously here; it aborts the run rather than becoming
                    // an ordinary failed observation of one database.
                    var invocation = SchemaValidationInvocation.Capture(provider!, target.Validation);
                    var validation = invocation.ExecuteAsync(cancellationToken);
                    try { result = await validation.ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception failure)
                    {
                        if (invocation.CallbackFailure is not null || invocation.ConfigurationFailure is not null || invocation.InvariantFailure is not null) throw;
                        operational = failure;
                    }
                }
                var outcome = new DataLinqSchemaStartupOutcome(target.Identity, target.Mode, result, operational);
                outcomes.Add(outcome);
                Log(outcome);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception failure)
        {
            // In particular, never invoke a failed logger again to report its
            // own exception. Completed outcomes stay available in the wrapper.
            throw new DataLinqSchemaStartupException(outcomes, failure);
        }
        if (outcomes.Any(outcome => outcome.PreventsStartup)) throw new DataLinqSchemaStartupException(outcomes);
    }

    private void Log(DataLinqSchemaStartupOutcome outcome)
    {
        if (outcome.OperationalFailure is { } failure)
        {
            var context = DataLinqFailure.GetContext(failure);
            logger.LogError(failure, "Schema validation for {Target} failed at {Stage}: {Message}",
                outcome.Target.DisplayName, context?.Stage, failure.Message);
            return;
        }
        var result = outcome.Result!;
        logger.Log(result.HasFailures ? LogLevel.Warning : LogLevel.Information,
            "Schema validation for {Target}, database {DatabaseName} ({DatabaseType}): {ModelTableCount} model objects, {DatabaseTableCount} database objects, {DifferenceCount} differences, {IssueCount} issues, HasFailures={HasFailures}, mode={Mode}",
            outcome.Target.DisplayName, result.DatabaseName, result.DatabaseType, result.ModelTableCount, result.DatabaseTableCount,
            result.Differences.Count, result.Issues.Count, result.HasFailures, outcome.Mode);
        foreach (var difference in result.Differences)
            logger.Log(difference.Severity switch
            {
                SchemaDifferenceSeverity.Error => LogLevel.Error,
                SchemaDifferenceSeverity.Warning => LogLevel.Warning,
                _ => LogLevel.Information
            }, "Schema difference for {Target} at {Path} ({Kind}, {Severity}): {Message}",
                outcome.Target.DisplayName, difference.Path, difference.Kind, difference.Severity, difference.Message);
        foreach (var issue in result.Issues)
            logger.Log(issue.Severity switch
            {
                DataLinqDiagnosticSeverity.Error => LogLevel.Error,
                DataLinqDiagnosticSeverity.Warning => LogLevel.Warning,
                _ => LogLevel.Information
            }, "Schema metadata issue for {Target} at {Path} ({FailureType}, {Severity}): {Message}",
                outcome.Target.DisplayName, issue.ObjectPath, issue.FailureType, issue.Severity, issue.Message);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
