using DataLinq;
using DataLinq.Core.Factories;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.SQLite;
using PackedGenerated;

var name = "w4_session_" + Guid.NewGuid().ToString("N");
await using var database = new SQLiteDatabase<AdvancedDatabase>($"Data Source={name};Mode=Memory;Cache=Shared", name);
var created = await PluginHook.CreateDatabaseFromMetadataAsync(DatabaseType.SQLite, database.Provider.Metadata, name, database.Provider.ConnectionString, true);
if (created.HasFailed) throw new Exception(created.Failure.ToString());
await database.InsertAsync(new MutableParent { Id = 3 });
await using IDataLinqUnitOfWork<AdvancedDatabase> owner = database.Transaction();
IDataLinqSession<AdvancedDatabase> session = owner;
var child = new MutableChild { Tenant = 7, Id = new(17), Name = "provisional", ParentId = 3 };
Child added = child.Insert(changes: row => row.Name = "added", transaction: session);
Child edited = await added.UpdateAsync(changes: row => row.Name = "edited", transaction: session, cancellationToken: default);
Child saved = session.Save(model: edited, changes: (MutableChild row) => row.Name = "saved");
if (saved.Name != "saved" || (await session.GetAsync<Child>(saved.PrimaryKeys()))?.Name != "saved")
    throw new Exception("Participant mutation/lookup did not share the owner.");
var scalar = database.PrepareQuery(7, tenant => database.Query().Children.Count(row => row.Tenant == tenant));
var rowQuery = database.PrepareQuery(7, tenant => database.Query().Children.SingleOrDefault(row => row.Tenant == tenant));
var sequence = database.PrepareSequenceQuery(new[] { 7 }, tenants => database.Query().Children.Where(row => tenants.Contains(row.Tenant)));
if (session.Execute(scalar, 7) != 1 || await session.ExecuteAsync(scalar, 7) != 1 ||
    session.Execute(rowQuery, 7)?.Name != "saved" || (await session.ExecuteAsync(rowQuery, 7))?.Name != "saved")
    throw new Exception("Prepared scalar/row did not use the supplied participant.");
var tenants = new[] { 7 };
var pending = session.Execute(sequence, tenants);
var pendingAsync = session.ExecuteAsync(sequence, tenants);
tenants[0] = 99;
if (pending.Single().Tenant != 7 || (await pendingAsync.ToArrayAsync()).Single().Tenant != 7)
    throw new Exception("Prepared invocation values were not captured.");
TransactionResult<string?> nullable = (string?)null;
if (!nullable.TryUnwrap(out var missing, out _) || missing is not null) throw new Exception("Nullable success changed case.");
TransactionResult<DataLinq.Mutation.Unit> empty = TransactionResult.Success();
if (!empty.IsSuccess) throw new Exception("No-value success was uninitialized.");
var markerPayload = new Rollback<string>("marker as data");
var markerSuccess = TransactionResult<Rollback<string>, string>.FromSuccess(markerPayload);
var markerFailure = TransactionResult<Rollback<string>, string>.FromRollback(markerPayload);
TransactionResult<Rollback<string>, string> wrappedMarkerSuccess = TransactionResult.Success(markerPayload);
if (markerSuccess.SuccessValue.Reason != "marker as data" || markerFailure.IsSuccess || !wrappedMarkerSuccess.IsSuccess)
    throw new Exception("Explicit marker-shaped result construction changed cases.");
TransactionResult<string, string> result = session.RequestRollback("business veto");
if (result.IsSuccess || !owner.TryGetRollbackReason<string>(out var reason) || reason != "business veto")
    throw new Exception("Participant veto was not shared with its owner.");
try { await owner.CommitAsync(); throw new Exception("Vetoed commit returned successfully."); }
catch (DataLinqRollbackRequestedException) { }
await owner.RollbackAsync();
if (await database.GetAsync<Child>(saved.PrimaryKeys()) is not null) throw new Exception("Participant independently committed.");

// A concrete, inferred transaction retains the existing mutation call spelling.
await using var typed = database.Transaction();
var independent = await new MutableChild { Tenant = 7, Id = new(18), Name = "typed", ParentId = 3 }
    .InsertAsync(changes: row => row.Name = "typed inserted", transaction: typed);
await independent.SaveAsync(changes: row => row.Name = "typed saved", transaction: typed);
await typed.RollbackAsync();
IDataLinqUnitOfWorkFactory<AdvancedDatabase> factory = new DataLinqUnitOfWorkFactory<AdvancedDatabase>(database);
if (factory.Execute(session => session.Query().Parents.Count()) != 1 ||
    await factory.ExecuteAsync((session, token) => session.Query().Parents.CountAsync(cancellationToken: token).AsTask()) != 1)
    throw new Exception("Factory-owned callbacks did not preserve ordinary results.");
factory.Execute(_ => { });
await factory.ExecuteAsync(_ => Task.CompletedTask);
var noValue = factory.ExecuteResult(_ => (TransactionResult<Unit>)TransactionResult.Success());
if (!noValue.IsSuccess) throw new Exception("No-value success was rejected.");
var typedOutcome = await factory.ExecuteResultAsync((session, _) =>
    Task.FromResult((TransactionResult<Unit, int>)session.RequestRollback(17)));
if (typedOutcome.Reason != 17) throw new Exception("Typed rollback did not survive owned cleanup.");
var stringOutcome = await factory.ExecuteResultAsync(session =>
    Task.FromResult((TransactionResult<string?>)session.RequestRollback("mapped")));
if (stringOutcome.Reason != "mapped") throw new Exception("String rollback did not survive owned cleanup.");
using (var manual = factory.Create())
{
    if (((Transaction)manual).Status != DatabaseTransactionStatus.Closed)
        throw new Exception("Factory creation eagerly opened a provider transaction.");
    manual.Rollback();
}
await RegistrationConsumer.Run();
await ConfigurationConsumer.Run();
await SessionConsumer.ServiceConsumer.Run();
Console.WriteLine($"W4 session consumer passed on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}.");
