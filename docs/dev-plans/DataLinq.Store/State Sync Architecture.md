> [!WARNING]
> This document is incubating architecture material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store State Sync Architecture

**Status:** Draft architecture.

## Purpose

This document describes how DataLinq.Store should synchronize server-authorized state modules into a client state store.

The central rule is:

> Sync module graphs, not raw database rows or opaque server cache objects.

Server result-set caching can decide whether a computed module snapshot is still valid. DataLinq.Store should hydrate valid module snapshots and later apply precise module patches or explicit invalidations.

## Actors

```text
Database
  Durable source of truth.

Server DataLinq runtime
  Executes authorized queries and mutations.

Server result cache
  Tracks dependencies for computed module snapshots and decides whether cached module snapshots are still valid.

Sync server
  Owns client module subscriptions, authorization checks, snapshots, patches, invalidations, and reconnect state.

Client DataLinq.Store
  Maintains module graphs, nodes, edges, local snapshots, and subscriptions.

UI framework
  Blazor, JavaScript, React, Vue, Svelte, or another consumer of client state.
```

## Module Subscription Flow

The first version should prefer named module subscriptions over arbitrary client-provided query expressions.

Example concept:

```csharp
await store.SubscribeAsync(
    Modules.ProjectWorkspace(projectId),
    snapshot => Render(snapshot));
```

Wire flow:

```text
Client -> Server
  Subscribe {
    ModuleId: "ProjectWorkspace",
    ModuleVersion: 3,
    Parameters: { ProjectId: 42 },
    ClientSchemaVersion: "...",
    LastKnownSequence: "..."
  }

Server -> Client
  ModuleSnapshot {
    SubscriptionId: "...",
    ModuleId: "ProjectWorkspace",
    ModuleVersion: 3,
    ParametersHash: "...",
    SchemaVersion: "...",
    Sequence: "...",
    Nodes: {...},
    Edges: {...},
    FreshnessToken: "..."
  }
```

The client stores the module graph by module identity, node type, node key, and edge name. UI code reads projections from the loaded module graph.

## Patch Flow

After a server-side mutation or CDC event, the sync server decides whether an active module subscription can be updated with a patch or must be invalidated.

Patch example:

```text
Server -> Client
  ModulePatch {
    ModuleId: "ProjectWorkspace",
    Sequence: "1024",
    NodesUpserted: [
      { Type: "TaskCard", Key: "t_1", Fields: {...} }
    ],
    NodesRemoved: [],
    EdgeChanges: [
      {
        Source: "ProjectHeader:p_42",
        Edge: "tasks",
        Remove: [],
        InsertOrMove: [
          { Target: "TaskCard:t_1", Index: 0 }
        ]
      }
    ]
  }
```

Client application is transactional:

1. validate schema and sequence expectations
2. upsert/remove module nodes
3. update module edges
4. publish affected module and subscription notifications
5. mark patch sequence as applied

UI observers should see one coherent state change, not a stream of half-applied node and edge updates.

## Invalidation Flow

When precision cannot be proven, invalidate instead of pretending.

```text
Server -> Client
  ModuleInvalidate {
    ModuleId: "ProjectWorkspace",
    SubscriptionId: "...",
    Reason: "Changed columns affected an unknown relation/index path.",
    Sequence: "1025",
    Refetch: true
  }
```

The client marks the module subscription stale and either refetches automatically or exposes stale state to the UI according to subscription policy.

Over-invalidation is acceptable. Under-invalidation is a correctness bug.

## Server Responsibilities

The server owns:

- authentication and authorization
- named module registry
- parameter validation
- DataLinq query execution
- module dependency tracking
- module freshness validation
- mutation execution
- post-commit publication
- patch versus invalidation decisions
- reconnect and replay behavior

The server must not publish cache changes before the database commit is durable.

## Client Responsibilities

The client owns:

- module graph storage
- subscription state
- node and edge storage
- local snapshot projection
- transactional patch application
- optimistic update overlays
- stale/loading/error state
- hydration and persistence when configured
- UI notification throttling or batching

The client should not decide whether it is authorized to see a server query. Authorization is a server contract.

## Named Modules

Named modules should be generated or registered explicitly. They need stable identity because identity is used by:

- authorization policies
- protocol compatibility
- client subscription state
- module-cache keys
- diagnostics
- generated TypeScript declarations

Sketch:

```csharp
[StateModule("ProjectWorkspace", Version = 3)]
public static StateModule<ProjectWorkspaceParams> ProjectWorkspace(
    StateModuleBuilder builder,
    ProjectDb db,
    ProjectId projectId) =>
    builder
        .Node("project", ProjectQueries.ProjectHeader(db, projectId))
        .Collection("tasks", ProjectQueries.TaskCards(db, projectId))
        .Build();
```

The syntax is not final. The principle is final: named, versionable module contracts beat arbitrary browser-supplied expression trees.

## Protocol Messages

Minimum message families:

```text
Subscribe
Unsubscribe
ModuleSnapshot
ModulePatch
ModuleInvalidate
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
- module id when relevant
- module version when relevant
- sequence or freshness token when relevant
- event id for deduplication when relevant

## Ordering And Freshness

The first protocol should make conservative promises:

- server mutations are published only after commit
- messages are sequenced per connection or per subscription
- clients must tolerate duplicate patches
- clients must detect gaps when sequence tokens skip unexpectedly
- a gap that cannot be replayed clears or refetches affected module subscriptions
- reconnect starts from the last acknowledged sequence when possible

Do not claim instant distributed consistency. The honest contract is explicit freshness, patching, invalidation, and refetch.

## Result Cache Integration

DataLinq's planned result-set caching uses explicit tracking scopes and dependency fingerprints. DataLinq.Store should make module snapshots the main concrete result shape for that model.

Initial server behavior can be blunt:

1. compute a module snapshot through a tracked module scope
2. store the module dependency fingerprint
3. on relevant invalidation, mark the module subscription stale
4. refetch and send a replacement module snapshot

Later behavior can become precise:

1. use changed row keys and old/new index values
2. decide whether affected module nodes or edges changed
3. send node and edge deltas
4. avoid full module refetch

The blunt version is still useful. A correct invalidate/refetch loop is better than a clever stale patcher.

## Optimistic Mutations

Optimistic updates should be command-based, not raw graph edits sent directly to the database.

Client flow:

```text
1. UI dispatches command.
2. Client applies optimistic overlay.
3. Server validates and executes command through application code/DataLinq mutation.
4. Server publishes authoritative patch.
5. Client commits, adjusts, or rolls back optimistic state.
```

The overlay should be visibly separate from authoritative module graph state so rejected commands can be rolled back without corrupting server state.

The detailed mutation design lives in [Mutation and Invalidation Loop](Mutation%20and%20Invalidation%20Loop.md). This architecture document only needs the high-level contract: commands cross the wire, optimistic overlays are local predictions, and server patches are authoritative.

## Security Boundaries

The sync protocol must assume clients are hostile.

Therefore:

- clients cannot submit arbitrary LINQ
- clients cannot subscribe to unregistered module ids
- clients cannot choose fields outside the module contract
- clients cannot bypass server authorization by asking for raw table data
- parameters must be validated and authorization-checked
- module patches must only include data the client is authorized to see

If authorization changes while a subscription is active, the server sends `AuthorizationRevoked` and the client clears affected module graph state if it is no longer covered by another authorized subscription.

## Diagnostics

Minimum diagnostics:

- active subscriptions
- module snapshots stored
- nodes stored by module and type
- edges stored by module and name
- patches applied
- duplicate patches ignored
- invalidations received
- refetches requested
- sequence gaps detected
- optimistic commands pending/rejected/confirmed
- patch classifications: no-effect, field, node, edge, fragment refetch, full invalidation
- bytes received per message family
- render notifications emitted per subscription

Diagnostics need to exist in the first serious implementation because stale state bugs without visibility are miserable.

## First Useful Demo

The first demo should be deliberately small:

- one generated DataLinq model
- one server-hosted state module
- one mutation command
- one browser client
- in-memory client store
- server sends full module snapshot on subscribe
- server sends full replacement module snapshot after mutation
- client uses an optimistic overlay while the mutation is pending
- client hydrates the graph and updates one subscribed view

That proves the architecture without pretending patch precision, persistence, offline conflict resolution, or CDC are already solved.

## Open Questions

- Should the first server transport be SignalR, raw WebSocket, or Server-Sent Events?
- Should snapshots include full node values, field diffs, or generated compact field arrays?
- Should subscriptions be per named module only, or can a module expose several server-approved roots/projections?
- Should node visibility be tracked per subscription so graph state can be removed when the last authorized subscription disappears?
- Should patch sequencing be global per database, per connection, or per subscription in the first version?
- Should module edges support paging in the first version, or should paging be deferred until patch semantics are proven?
