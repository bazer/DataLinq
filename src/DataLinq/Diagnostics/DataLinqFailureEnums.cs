using System;

namespace DataLinq.Diagnostics;

/// <summary>The reported cause of a failure, based on execution evidence.</summary>
public enum DataLinqFailureCause
{
    /// <summary>Unknown.</summary>
    Unknown = 0,
    /// <summary>Cancellation.</summary>
    Cancellation = 1,
    /// <summary>Timeout.</summary>
    Timeout = 2,
    /// <summary>Provider Error.</summary>
    ProviderError = 3,
    /// <summary>Application Error.</summary>
    ApplicationError = 4,
    /// <summary>Materialization Error.</summary>
    MaterializationError = 5,
    /// <summary>Local Finalization Error.</summary>
    LocalFinalizationError = 6,
    /// <summary>Invalid Operation.</summary>
    InvalidOperation = 7,
}

/// <summary>The specific operation associated with a failure.</summary>
public enum DataLinqOperationKind
{
    /// <summary>Unknown.</summary>
    Unknown = 0,
    /// <summary>Query.</summary>
    Query = 1,
    /// <summary>Key Lookup.</summary>
    KeyLookup = 2,
    /// <summary>Relation Load.</summary>
    RelationLoad = 3,
    /// <summary>Insert.</summary>
    Insert = 4,
    /// <summary>Update.</summary>
    Update = 5,
    /// <summary>Save.</summary>
    Save = 6,
    /// <summary>Delete.</summary>
    Delete = 7,
    /// <summary>Commit.</summary>
    Commit = 8,
    /// <summary>Rollback.</summary>
    Rollback = 9,
    /// <summary>Dispose.</summary>
    Dispose = 10,
    /// <summary>Transaction Callback.</summary>
    TransactionCallback = 11,
    /// <summary>Raw Command.</summary>
    RawCommand = 12,
    /// <summary>Metadata Read.</summary>
    MetadataRead = 13,
    /// <summary>Schema Validation.</summary>
    SchemaValidation = 14,
    /// <summary>Existence Check.</summary>
    ExistenceCheck = 15,
    /// <summary>Provisioning.</summary>
    Provisioning = 16,
    /// <summary>Provider Configuration.</summary>
    ProviderConfiguration = 17,
}

/// <summary>The execution stage in which a failure occurred.</summary>
public enum DataLinqFailureStage
{
    /// <summary>Unknown.</summary>
    Unknown = 0,
    /// <summary>Validation.</summary>
    Validation = 1,
    /// <summary>Initialization.</summary>
    Initialization = 2,
    /// <summary>Command Execution.</summary>
    CommandExecution = 3,
    /// <summary>Row Loading.</summary>
    RowLoading = 4,
    /// <summary>Callback.</summary>
    Callback = 5,
    /// <summary>Local Finalization.</summary>
    LocalFinalization = 6,
    /// <summary>Commit.</summary>
    Commit = 7,
    /// <summary>Rollback.</summary>
    Rollback = 8,
    /// <summary>Cache Recovery.</summary>
    CacheRecovery = 9,
    /// <summary>Notification.</summary>
    Notification = 10,
    /// <summary>Cleanup.</summary>
    Cleanup = 11,
}

/// <summary>The certainty of transaction completion, independently of cleanup or lifecycle status.</summary>
public enum DataLinqCompletionOutcome
{
    /// <summary>Available evidence cannot establish whether transaction completion took effect.</summary>
    Unknown = 0,
    /// <summary>No applicable DataLinq transaction-completion contract. This does not imply absence of database effects.</summary>
    NotApplicable = 1,
    /// <summary>No completion attempt has occurred; earlier transaction writes may exist.</summary>
    NotAttempted = 2,
    /// <summary>Database commit was confirmed, independently of later local finalization or cleanup failure.</summary>
    Committed = 3,
    /// <summary>Rollback was confirmed and no earlier uncertain completion must be preserved.</summary>
    RolledBack = 4,
}

/// <summary>Actions permitted when a failure was reported; these are not retries or enduring permissions.</summary>
[Flags]
public enum DataLinqRecoveryActions
{
    /// <summary>No remaining recovery action is permitted by this snapshot.</summary>
    None = 0,
    /// <summary>Ordinary supported operations may continue after current-state validation; this does not authorize retries.</summary>
    Continue = 1,
    /// <summary>An explicit rollback may be attempted, subject to current-state validation.</summary>
    Rollback = 2,
    /// <summary>The owned transaction may be disposed.</summary>
    Dispose = 4,
    /// <summary>Await or properly finish/dispose the active operation or reader; do not abandon it or forcibly close its resources.</summary>
    FinishActiveOperation = 8,
}
