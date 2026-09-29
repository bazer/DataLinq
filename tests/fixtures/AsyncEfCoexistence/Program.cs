using DataLinq.Linq;
using Microsoft.EntityFrameworkCore;
using DlAsync = DataLinq.Linq.DataLinqAsyncQueryableExtensions;
using EfAsync = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions;

IQueryable<int> foreign = new[] { 1 }.AsQueryable();
#if EXPECT_AMBIGUITY
// This must fail with CS0121 when both extension namespaces are imported.
_ = await foreign.CountAsync();
#else
await Reject(() => DlAsync.CountAsync(foreign).AsTask());
await Reject(() => DlAsync.ToListAsync(foreign.Select(value => new { value })).AsTask());
await Reject(() => DlAsync.ToListAsync(foreign.Select(value => new Dto(value))).AsTask());
await Reject(() => DlAsync.ToListAsync(foreign.Select(value => (int?)value)).AsTask());
await Reject(() => DlAsync.ToListAsync(foreign.Select(value => (IValue)new Dto(value))).AsTask());
await Reject(() => EfAsync.CountAsync(foreign));
Console.WriteLine("EF/DataLinq static aliases bind independently on " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
#endif

#if !EXPECT_AMBIGUITY
static async Task Reject(Func<Task> action)
{
    try { await action(); }
    catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException) { return; }
    throw new Exception("Foreign query provider was accepted.");
}
#endif
internal interface IValue { int Value { get; } }
internal sealed record Dto(int Value) : IValue;
