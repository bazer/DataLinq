using System;
using System.Threading.Tasks;
using DataLinq.Mutation;

namespace DataLinq.Tests.Unit.Core;

public sealed class TransactionResultTests
{
    [Test]
    public async Task NullableSuccessAndNoValueSuccess_AreInitializedCases()
    {
        TransactionResult<string?, string> nullable = (string?)null;
        await Assert.That(nullable.TryUnwrap(out var value, out var failure)).IsTrue();
        await Assert.That(value).IsNull();
        await Assert.That(failure).IsNull();
        await Assert.That(nullable.SuccessValue).IsNull();
        TransactionResult<DataLinq.Mutation.Unit, int> empty = TransactionResult.Success();
        await Assert.That(empty.IsSuccess).IsTrue();
        TransactionResult<int?> number = (int?)null;
        await Assert.That(number.TryUnwrap(out var missing, out _)).IsTrue();
        await Assert.That(missing).IsNull();
        TransactionResult<int> zero = 0;
        await Assert.That(zero.SuccessValue).IsEqualTo(0);
    }

    [Test]
    public async Task SamePayloadTypesAndObjectSuccess_PreserveExplicitCase()
    {
        TransactionResult<string, string> success = "same";
        TransactionResult<string, string> rollback = new Rollback<string>("same");
        await Assert.That(success.IsSuccess).IsTrue();
        await Assert.That(rollback.IsSuccess).IsFalse();
        await Assert.That(rollback.TryUnwrap(out _, out var reason)).IsFalse();
        await Assert.That(reason).IsEqualTo("same");
        await Assert.That(rollback.Match(value => "success:" + value, error => "rollback:" + error)).IsEqualTo("rollback:same");
        TransactionResult<object, string> objectRollback = new Rollback<string>("reason");
        TransactionResult<object, string> boxedSuccess = (object)new Rollback<string>("data");
        await Assert.That(objectRollback.IsSuccess).IsFalse();
        await Assert.That(boxedSuccess.IsSuccess).IsTrue();
        TransactionResult<string> shorthand = "plain successful string";
        TransactionResult<string> shorthandRollback = new Rollback<string>("failure");
        await Assert.That(shorthand.SuccessValue).IsEqualTo("plain successful string");
        await Assert.That(shorthandRollback.Reason).IsEqualTo("failure");
    }

    [Test]
    public async Task UninitializedAndWrongCaseInspection_ThrowWithoutInventingPayloads()
    {
        var invalid = default(TransactionResult<int, string>);
        await Assert.That(() => invalid.IsSuccess).Throws<InvalidOperationException>();
        await Assert.That(() => invalid.TryUnwrap(out _, out _)).Throws<InvalidOperationException>();
        await Assert.That(() => invalid.SuccessValue).Throws<InvalidOperationException>();
        await Assert.That(() => invalid.Reason).Throws<InvalidOperationException>();
        await Assert.That(() => invalid.Match(value => value, _ => -1)).Throws<InvalidOperationException>();
        var shorthand = default(TransactionResult<string>);
        await Assert.That(() => shorthand.TryUnwrap(out _, out _)).Throws<InvalidOperationException>();
        TransactionResult<int, string> success = 12;
        TransactionResult<int, string> rollback = new Rollback<string>("no");
        await Assert.That(() => success.Reason).Throws<InvalidOperationException>();
        await Assert.That(() => rollback.SuccessValue).Throws<InvalidOperationException>();
        await Assert.That(() => new Rollback<string>(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => (TransactionResult<int, string>)default(Rollback<string>)).Throws<InvalidOperationException>();
        await Assert.That(() => (TransactionResult<int, int>)default(Rollback<int>)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MarkerShapedSuccessTypesHaveExplicitUnambiguousConstruction()
    {
        var marker = new Rollback<string>("payload");
        var success = TransactionResult<Rollback<string>, string>.FromSuccess(marker);
        var failure = TransactionResult<Rollback<string>, string>.FromRollback(marker);
        await Assert.That(success.IsSuccess).IsTrue();
        await Assert.That(success.SuccessValue.Reason).IsEqualTo("payload");
        await Assert.That(failure.IsSuccess).IsFalse();
        await Assert.That(failure.Reason).IsEqualTo("payload");
        TransactionResult<Rollback<string>, string> wrapped = TransactionResult.Success(marker);
        await Assert.That(wrapped.SuccessValue.Reason).IsEqualTo("payload");
        var shorthandSuccess = TransactionResult<Rollback<string>>.FromSuccess(marker);
        var shorthandFailure = TransactionResult<Rollback<string>>.FromRollback(marker);
        await Assert.That(shorthandSuccess.SuccessValue.Reason).IsEqualTo("payload");
        await Assert.That(shorthandFailure.Reason).IsEqualTo("payload");
        await Assert.That(() => TransactionResult<Rollback<string>>.FromRollback(default)).Throws<InvalidOperationException>();
        await Assert.That(typeof(TransactionResult<int>).GetProperty("Value")).IsNull();
        await Assert.That(typeof(TransactionResult<int>).GetProperty("HasValue")).IsNull();
    }
}
