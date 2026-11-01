using System;
using System.Threading.Tasks;
using DataLinq.ErrorHandling;
using DataLinq.Exceptions;
using DataLinq.Execution;
using DataLinq.Validation;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class SchemaValidationReadFailureTests
{
    [Test]
    public async Task DefinitionContextRemainsInTheSummaryAndOriginalExceptionKeepsItsStack()
    {
        Exception original;
        try { ThrowOriginal(); throw new InvalidOperationException("unreachable"); }
        catch (InvalidOperationException originalFailure) { original = originalFailure; }
        var definition = new MetadataDefinitionFactory().BuildProviderMetadata(
            new MetadataDatabaseDraft("DefinitionDb", new("DefinitionDb", "Tests", ModelCsType.Class))).ValueOrException();
        var tree = DLOptionFailure.Fail(DLFailureType.Exception, original, definition);
        var failure = await Assert.That(() => SchemaValidationReadFailure.Throw(tree)).Throws<DataLinqSchemaReadException>();
        await Assert.That(failure!.Message).Contains("DefinitionDb");
        await Assert.That(failure.Message).Contains(original.Message);
        await Assert.That(failure.Message.Contains(nameof(ThrowOriginal), StringComparison.Ordinal)).IsFalse();
        await Assert.That(failure.InnerException).IsSameReferenceAs(original);
        await Assert.That(original.StackTrace!).Contains(nameof(ThrowOriginal));
        await Assert.That(failure.Failure).IsSameReferenceAs(tree);
    }

    [Test]
    public async Task PlainExceptionKeepsIdentityStackAndExecutionEvidence()
    {
        Exception original;
        try { ThrowOriginal(); throw new InvalidOperationException("unreachable"); }
        catch (InvalidOperationException failure) { original = failure; }
        var context = new ExecutionFailureContext(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [], operation: ExecutionOperationKind.MetadataRead);
        ExecutionFailureContexts.Attach(original, context);
        var observed = await Assert.That(() => SchemaValidationReadFailure.Throw(DLOptionFailure.Fail(DLFailureType.Exception, original)))
            .Throws<InvalidOperationException>();
        await Assert.That(observed).IsSameReferenceAs(original);
        await Assert.That(original.StackTrace!).Contains(nameof(ThrowOriginal));
        await Assert.That(ExecutionFailureContexts.Get(original)).IsSameReferenceAs(context);
    }

    [Test]
    public async Task CompositeAndExceptionFreeFailuresKeepOriginalTreesAndOrderedCleanup()
    {
        var original = new InvalidOperationException("native failure with raw details");
        var cleanup = new ApplicationException("cleanup details");
        ExecutionFailureContexts.Attach(original, new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null,
            [new(ExecutionFailureCause.Unknown, ExecutionFailureStage.Cleanup, cleanup)]));
        var local = DLOptionFailure.Fail(DLFailureType.InvalidModel, "Original model path 'a.b' and message");
        var tree = DLOptionFailure.AggregateFail([local, DLOptionFailure.Fail(DLFailureType.Exception, (Exception)original)]);
        var failure = await Assert.That(() => SchemaValidationReadFailure.Throw(tree)).Throws<DataLinqSchemaReadException>();
        await Assert.That(failure!.Failure).IsSameReferenceAs(tree);
        await Assert.That(failure.Exceptions[0]).IsSameReferenceAs(original);
        await Assert.That(failure.Exceptions[1]).IsSameReferenceAs(cleanup);
        await Assert.That(((AggregateException)failure.InnerException!).InnerExceptions[0]).IsSameReferenceAs(original);
        await Assert.That(failure.Message).Contains(local.Message);
        var noException = await Assert.That(() => SchemaValidationReadFailure.Throw(local)).Throws<DataLinqSchemaReadException>();
        await Assert.That(noException!.Failure).IsSameReferenceAs(local);
        await Assert.That(noException.InnerException).IsNull();
        await Assert.That(noException.Exceptions).IsEmpty();
    }

    private static void ThrowOriginal() => throw new InvalidOperationException("original provider message");
}
