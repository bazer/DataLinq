using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.ErrorHandling;
using DataLinq.Logging;
using DataLinq.Metadata;
using ThrowAway;

namespace DataLinq.Tests.Unit.Core;

public class DatabaseDefinitionResolverTests
{
    [Test]
    public async Task DatabaseProvider_PreservesProtectedSevenParameterConstructorContract()
    {
        var constructor = typeof(DatabaseProvider).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types:
            [
                typeof(string),
                typeof(Type),
                typeof(DatabaseType),
                typeof(DataLinqLoggingConfiguration),
                typeof(string),
                typeof(Func<Option<DatabaseDefinition, IDLOptionFailure>>),
                typeof(bool)
            ],
            modifiers: null);

        await Assert.That(constructor).IsNotNull();
        await Assert.That(constructor!.IsFamily).IsTrue();

        var parameters = constructor.GetParameters();
        await Assert.That(parameters[4].IsOptional).IsTrue();
        await Assert.That(parameters[4].DefaultValue).IsNull();
        await Assert.That(parameters[5].IsOptional).IsTrue();
        await Assert.That(parameters[5].DefaultValue).IsNull();
        await Assert.That(parameters[6].IsOptional).IsTrue();
        await Assert.That((bool)parameters[6].DefaultValue!).IsTrue();
    }

    [Test]
    [NotInParallel]
    public async Task ResolveLoadedDatabase_ConcurrentCallerCannotReturnWhileFirstBinderIsRunning()
    {
        var databaseModelType = typeof(ResolverMarker);
        DatabaseDefinition.TryRemoveLoadedDatabase(databaseModelType, out _);

        using var firstBinderEntered = new ManualResetEventSlim(false);
        using var releaseFirstBinder = new ManualResetEventSlim(false);
        using var secondResolverStarted = new ManualResetEventSlim(false);
        using var secondResolverReturned = new ManualResetEventSlim(false);

        var winningMetadata = new DatabaseDefinition(
            "ResolverGate",
            new CsTypeDeclaration(databaseModelType));
        var factoryCalls = 0;
        var binderCalls = 0;
        Task<DatabaseDefinition>? firstResolution = null;
        Thread? secondResolutionThread = null;
        DatabaseDefinition? secondResolvedMetadata = null;
        Exception? secondResolutionException = null;

        try
        {
            firstResolution = Task.Run(() => DatabaseDefinition.ResolveLoadedDatabase(
                databaseModelType,
                () =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    return winningMetadata;
                },
                _ =>
                {
                    Interlocked.Increment(ref binderCalls);
                    firstBinderEntered.Set();
                    if (!releaseFirstBinder.Wait(TimeSpan.FromSeconds(30)))
                        throw new TimeoutException("The test did not release the first metadata binder.");
                }));

            if (!firstBinderEntered.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The first metadata binder was not reached.");

            secondResolutionThread = new Thread(() =>
            {
                secondResolverStarted.Set();
                try
                {
                    secondResolvedMetadata = DatabaseDefinition.ResolveLoadedDatabase(
                        databaseModelType,
                        () =>
                        {
                            Interlocked.Increment(ref factoryCalls);
                            return new DatabaseDefinition(
                                "LosingResolverCandidate",
                                new CsTypeDeclaration(databaseModelType));
                        },
                        _ => Interlocked.Increment(ref binderCalls));
                }
                catch (Exception exception)
                {
                    secondResolutionException = exception;
                }
                finally
                {
                    secondResolverReturned.Set();
                }
            })
            {
                IsBackground = true,
                Name = "DataLinq metadata resolver concurrency test"
            };
            secondResolutionThread.Start();

            if (!secondResolverStarted.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The competing metadata resolver was not scheduled.");

            var blockedOrReturned = SpinWait.SpinUntil(
                () =>
                    (secondResolutionThread.ThreadState & ThreadState.WaitSleepJoin) != 0 ||
                    secondResolverReturned.IsSet,
                TimeSpan.FromSeconds(5));
            if (!blockedOrReturned)
                throw new TimeoutException("The competing metadata resolver neither blocked nor returned.");

            await Assert.That(
                    (secondResolutionThread.ThreadState & ThreadState.WaitSleepJoin) != 0)
                .IsTrue();
            await Assert.That(secondResolverReturned.IsSet).IsFalse();

            releaseFirstBinder.Set();
            var firstResolvedMetadata = await firstResolution;
            if (!secondResolutionThread.Join(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The competing metadata resolver did not finish after binding was released.");

            await Assert.That(secondResolutionException).IsNull();
            await Assert.That(firstResolvedMetadata).IsSameReferenceAs(winningMetadata);
            await Assert.That(secondResolvedMetadata).IsSameReferenceAs(winningMetadata);
            await Assert.That(factoryCalls).IsEqualTo(1);
            await Assert.That(binderCalls).IsEqualTo(2);
        }
        finally
        {
            releaseFirstBinder.Set();

            if (secondResolutionThread is { IsAlive: true })
                secondResolutionThread.Join(TimeSpan.FromSeconds(5));

            if (firstResolution is not null)
            {
                try
                {
                    await firstResolution;
                }
                catch
                {
                    // Preserve the original assertion or timeout while still observing worker failures.
                }
            }

            DatabaseDefinition.TryRemoveLoadedDatabase(databaseModelType, out _);
        }
    }

    private sealed class ResolverMarker;
}
