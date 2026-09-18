using System;
using DataLinq.Execution;
using DataLinq.Instances;

namespace DataLinq.Mutation;

public partial class StateChange
{
    internal bool NeedsGeneratedValue => Type == TransactionChangeType.Insert && HasAutoIncrement && PrimaryKeys.IsNull;

    internal CapturedSql CaptureAsyncStatement(Transaction transaction)
    {
        var sql = CapturedSql.Capture(PrepareExecutionQuery(transaction).ToSql());
        EnsureCapturedMutationUnchanged("asynchronous command capture");
        return sql;
    }

    internal void CompleteAsyncStatement(object? generatedValue, MutationInputReservation reservation)
    {
        EnsureCapturedMutationUnchanged("asynchronous statement execution");
        executionPhase = StateChangeExecutionPhase.Hydration;
        if (NeedsGeneratedValue && Table.AutoIncrementPrimaryKeyColumn is { } column)
        {
            var canonical = GeneratedValueDecoder.DecodeAutoIncrementValue(column, generatedValue, "sql.generated");
            reservation.SetGeneratedValue(column, ProviderRowMaterializer.MaterializeValue(column, canonical, "sql.generated"));
        }
        FinalizePrimaryKeysAfterExecution();
        FinalizeRelationKeysAfterExecution();
        CaptureFinalizedMutation();
        executionPhase = StateChangeExecutionPhase.Completed;
    }
}
