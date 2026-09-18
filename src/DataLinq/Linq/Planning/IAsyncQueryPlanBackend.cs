using System.Collections.Generic;
using System.Threading.Tasks;

namespace DataLinq.Linq.Planning;

/// <summary>Explicit execution capability; implementing a synchronous backend never implies async support.</summary>
internal interface IAsyncQueryPlanBackend : IQueryPlanBackend
{
    IAsyncEnumerable<T> ExecuteSequenceAsync<T>(ValidatedQueryExecutionRequest request);
    Task<T> ExecuteAsync<T>(ValidatedQueryExecutionRequest request);
}
