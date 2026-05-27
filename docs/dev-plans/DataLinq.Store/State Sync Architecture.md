> [!WARNING]
> This document is incubating architecture material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store State Sync Architecture

**Status:** Draft architecture.

## Purpose

This document describes how DataLinq.Store should synchronize server-authorized DataLinq results into a client state store.

The central rule is:

> Sync normalized data and query memberships, not opaque server cache objects.

Server result-set caching can decide whether a computed result is still valid. DataLinq.Store should turn valid results into client snapshots and later apply precise patches or explicit invalidations.

## Actors

```text
Database
  Durable source of truth.

Server DataLinq runtime
  Executes authorized queries and mutations.

Server result cache
  Tracks dependencies for computed results and decides whether cached results are still valid.

Sync server
  Owns client subscriptions, authorization checks, snapshots, patches, invalidations, and reconnect state.

Client DataLinq.Store
  Maintains normalized rows, query memberships, local snapshots, and subscriptions.

UI framework
  Blazor, JavaScript, React, Vue, Svelte, or another consumer of client state.
```

## Subscription Flow

The first version should prefer named query subscriptions over arbitrary client-provided query expressions.

Example concept:

```csharp
await store.SubscribeAsync(
    Queries.EmployeesByDepartment(departmentId),
    snapshot => Render(snapshot));
```

Wire flow:

```text
Client -> Server
  Subscribe {
    QueryId: "EmployeesByDepartment",
    Parameters: { DepartmentId: 4 },
    ClientSchemaVersion: "...",
    LastKnownSequence: "..."
  }

Server -> Client
  Snapshot {
    SubscriptionId: "...",
    QueryId: "EmployeesByDepartment",
    ParametersHash: "...",
    SchemaVersion: "...",
    Sequence: "...",
    Rows: [...],
    Membership: [Employee:1, Employee:2],
    FreshnessToken: "..."
  }
```

The client stores the rows by table/key and stores the membership under the subscription. UI code reads a projection from the normalized store.

## Patch Flow

After a server-side mutation or CDC event, the sync server decides whether an active subscription can be updated with a patch or must be invalidated.

Patch example:

```text
Server -> Client
  Patch {
    Sequence: "1024",
    RowsUpserted: [
      { Table: "Employee", Key: 1, Values: {...} }
    ],
    RowsRemoved: [],
    MembershipChanges: [
      {
        SubscriptionId: "...",
        Remove: [],
        InsertOrMove: [
          { Key: Employee:1, Index: 0 }
        ]
      }
    ]
  }
```

Client application is transactional:

1. validate schema and sequence expectations
2. upsert/remove normalized rows
3. update membership lists
4. publish affected table and subscription notifications
5. mark patch sequence as applied

UI observers should see one coherent state change, not a stream of half-applied row and membership updates.

## Invalidation Flow

When precision cannot be proven, invalidate instead of pretending.

```text
Server -> Client
  Invalidate {
    SubscriptionId: "...",
    Reason: "Changed columns affected an unknown relation/index path.",
    Sequence: "1025",
    Refetch: true
  }
```

The client marks the subscription stale and either refetches automatically or exposes stale state to the UI according to subscription policy.

Over-invalidation is acceptable. Under-invalidation is a correctness bug.

## Server Responsibilities

The server owns:

- authentication and authorization
- named query registry
- parameter validation
- DataLinq query execution
- result dependency tracking
- result freshness validation
- mutation execution
- post-commit publication
- patch versus invalidation decisions
- reconnect and replay behavior

The server must not publish cache changes before the database commit is durable.

## Client Responsibilities

The client owns:

- normalized row storage
- subscription state
- query membership storage
- local snapshot projection
- transactional patch application
- optimistic update overlays
- stale/loading/error state
- hydration and persistence when configured
- UI notification throttling or batching

The client should not decide whether it is authorized to see a server query. Authorization is a server contract.

## Named Queries

Named queries should be generated or registered explicitly. They need stable identity because identity is used by:

- authorization policies
- protocol compatibility
- client subscription state
- result-cache keys
- diagnostics
- generated TypeScript declarations

Sketch:

```csharp
[StoreQuery("EmployeesByDepartment")]
public static IQueryable<Employee> EmployeesByDepartment(
    EmployeesDb db,
    int departmentId) =>
    db.Employees.Where(employee => employee.DepartmentId == departmentId);
```

The syntax is not final. The principle is final: named, versionable query contracts beat arbitrary browser-supplied expression trees.

## Protocol Messages

Minimum message families:

```text
Subscribe
Unsubscribe
Snapshot
Patch
Invalidate
Ack
Resync
Error
Heartbeat
SchemaMismatch
AuthorizationRevoked
```

All messages should carry enough information for diagnostics:

- protocol version
- schema version
- database id
- subscription id when relevant
- query id when relevant
- sequence or freshness token when relevant
- event id for deduplication when relevant

## Ordering And Freshness

The first protocol should make conservative promises:

- server mutations are published only after commit
- messages are sequenced per connection or per subscription
- clients must tolerate duplicate patches
- clients must detect gaps when sequence tokens skip unexpectedly
- a gap that cannot be replayed clears or refetches affected subscriptions
- reconnect starts from the last acknowledged sequence when possible

Do not claim instant distributed consistency. The honest contract is explicit freshness, patching, invalidation, and refetch.

## Result Cache Integration

DataLinq's planned result-set caching uses explicit tracking scopes and dependency fingerprints. DataLinq.Store should consume that model, not bypass it.

Initial server behavior can be blunt:

1. compute snapshot through a tracked result scope
2. store result dependency fingerprint
3. on relevant invalidation, mark subscription stale
4. refetch and send a replacement snapshot

Later behavior can become precise:

1. use changed row keys and old/new index values
2. decide whether the row still belongs in the result
3. send membership delta
4. avoid full result refetch

The blunt version is still useful. A correct invalidate/refetch loop is better than a clever stale patcher.

## Optimistic Mutations

Optimistic updates should be command-based, not raw row edits sent directly to the database.

Client flow:

```text
1. UI dispatches command.
2. Client applies optimistic overlay.
3. Server validates and executes command through application code/DataLinq mutation.
4. Server publishes authoritative patch.
5. Client commits, adjusts, or rolls back optimistic state.
```

The overlay should be visibly separate from authoritative normalized rows so rejected commands can be rolled back without corrupting server state.

## Security Boundaries

The sync protocol must assume clients are hostile.

Therefore:

- clients cannot submit arbitrary LINQ
- clients cannot subscribe to unregistered query ids
- clients cannot choose columns outside the query contract
- clients cannot bypass server authorization by asking for raw table data
- parameters must be validated and authorization-checked
- row patches must only include data the client is authorized to see

If authorization changes while a subscription is active, the server sends `AuthorizationRevoked` and the client clears affected rows if they are no longer covered by another authorized subscription.

## Diagnostics

Minimum diagnostics:

- active subscriptions
- rows stored by table
- query memberships stored
- patches applied
- duplicate patches ignored
- invalidations received
- refetches requested
- sequence gaps detected
- optimistic commands pending/rejected/confirmed
- bytes received per message family
- render notifications emitted per subscription

Diagnostics need to exist in the first serious implementation because stale state bugs without visibility are miserable.

## First Useful Demo

The first demo should be deliberately small:

- one generated DataLinq model
- one server-hosted named query
- one mutation command
- one browser client
- in-memory client store
- server sends full snapshot on subscribe
- server sends full replacement snapshot after mutation
- client normalizes rows and updates one subscribed view

That proves the architecture without pretending patch precision, persistence, offline conflict resolution, or CDC are already solved.

## Open Questions

- Should the first server transport be SignalR, raw WebSocket, or Server-Sent Events?
- Should snapshots include full row values, column diffs, or generated compact field arrays?
- Should subscriptions be per named query only, or can a named query expose several server-approved projections?
- Should row visibility be tracked per subscription so rows can be removed when the last authorized subscription disappears?
- Should patch sequencing be global per database, per connection, or per subscription in the first version?
- Should query result membership support paging in the first version, or should paging be deferred until patch semantics are proven?
