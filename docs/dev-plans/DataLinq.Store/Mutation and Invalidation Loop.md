> [!WARNING]
> This document is incubating architecture material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store Mutation And Invalidation Loop

**Status:** Draft specification.

## Purpose

DataLinq.Store needs a mutation model that follows DataLinq's existing philosophy:

- reads are immutable snapshots
- writes are explicit
- transactions are visible in the programming model
- no-op writes should not cause pointless work
- precise invalidation is preferred when it can be proven
- conservative invalidation is better than stale state

The Store version adds one more requirement:

> A write should invalidate or rerender the smallest defensible part of the module graph.

The target loop is:

```text
Client command
  -> optimistic module overlay
  -> server command handler
  -> DataLinq transaction
  -> mutation impact record
  -> module impact analysis
  -> ModulePatch or ModuleInvalidate
  -> client reconciliation
  -> minimal UI notification
```

## Design Rules

### Commands Cross The Wire

Clients should send commands, not modified module graphs.

Good:

```text
RenameTask {
  TaskId: "t_1",
  Title: "Fix the login redirect",
  ExpectedVersion: "1024"
}
```

Bad:

```text
ProjectWorkspace {
  ...the entire edited module graph...
}
```

Commands are easier to authorize, validate, audit, retry, reject, and map to DataLinq transactions.

### Server Transactions Are Authoritative

The client can predict state. The server decides state.

Server command handlers should execute through ordinary application code and DataLinq mutation APIs. After commit, the server produces authoritative module effects.

The server must not publish module patches before the database transaction commits.

### Optimism Is An Overlay

Optimistic client updates should not directly mutate authoritative module graph state.

The client should maintain:

```text
Authoritative graph
  Last server-confirmed module snapshots and patches.

Optimistic overlay
  Pending local command effects keyed by command id.

Derived selectors
  Read authoritative graph plus optimistic overlay.
```

This makes rollback straightforward. If the server rejects a command, remove the overlay and notify affected selectors. If the server confirms with a different authoritative result, replace the overlay with the server patch.

### Patch Only When Proven

The server should emit precise patches only when module impact analysis proves the effect.

Fallback order:

```text
No effect
Field patch
Node upsert/remove
Edge patch
Fragment refetch
Full module invalidate/refetch
```

Under-invalidation is a correctness bug. Over-invalidation is a performance cost.

## Mutation Inputs

The server needs enough detail from a committed mutation to classify module effects.

The mutation impact record should include:

- database id
- transaction id or commit sequence
- table identity
- operation: insert, update, delete
- provider primary key
- changed columns
- old values for relation/index/filter/sort keys when available
- new values for relation/index/filter/sort keys when available
- generated client key mapping when affected
- authorization context when module visibility may change

DataLinq already has local `StateChange` mechanics. The Store/sync layer should not require fake `StateChange` objects for remote or module-level invalidation, but it should consume the same kind of information: what table changed, which key changed, and which columns/index values changed.

## Module Impact Matrix

Each module definition should generate or register a module impact matrix.

The matrix maps database dependencies to module graph effects:

```text
Table.Column -> projected node field
Table.Column -> module filter predicate
Table.Column -> edge key
Table.Column -> sort key
Table.Column -> grouping key
Table.Column -> aggregate
Table.Column -> authorization predicate
Table.Column -> opaque key mapping
```

Example:

```text
Task.Title
  affects TaskCard.Title field
  patch: SetField(TaskCard:{task}, Title)

Task.Status
  affects TaskCard.Status field
  affects ProjectHeader.activeTasks edge membership
  patch: SetField + edge remove/insert/move

Task.ProjectId
  affects ProjectWorkspace(project).tasks edge
  requires old and new ProjectId
  patch old module: remove task edge item
  patch new module: add task edge item if subscribed and authorized

Task.InternalAuditNote
  not exposed in ProjectWorkspace
  patch: no effect
```

This matrix is the heart of efficient invalidation. Without it, every mutation becomes a blunt module refresh.

## Effect Classification

### No Effect

The mutation touched data that no active module uses.

Action:

- publish nothing to the client
- optionally record diagnostics that the mutation had no module impact

This should be common in real applications.

### Field Patch

The mutation changes an exposed field on an existing node.

Action:

```text
SetField(TaskCard:t_1, Title, "Fix the login redirect")
```

Client notification:

- notify the node
- notify selectors that read that field
- do not notify unrelated module edges

### Node Upsert

An inserted or updated row now produces a module node.

Action:

```text
UpsertNode(TaskCard:t_1, fields...)
```

This may also require edge insertion if the node belongs to a collection.

### Node Remove

A deleted row or changed predicate means a node no longer belongs to the module.

Action:

```text
RemoveNode(TaskCard:t_1)
RemoveEdgeItem(ProjectHeader:p_42.tasks, TaskCard:t_1)
```

If a node is shared by multiple module edges or roots, removal needs reference/visibility accounting. The safe first implementation can keep node lifetime module-scoped.

### Edge Patch

A relation key, grouping key, sort key, or membership predicate changed.

Actions:

```text
InsertEdgeItem(...)
RemoveEdgeItem(...)
MoveEdgeItem(...)
ReplaceEdge(...)
```

Precise edge patches require old and new values. Without old values, fall back to replacing the edge or invalidating the module.

### Fragment Refetch

A small subgraph can be recomputed more cheaply than the full module.

Example:

```text
RefetchEdge(ProjectHeader:p_42.tasks)
```

This is a later optimization. It requires server APIs that can recompute one module fragment with the same authorization and dependency rules as the full module.

### Full Module Invalidate

The server cannot prove a precise patch.

Action:

```text
ModuleInvalidate(ProjectWorkspace(project:42), Reason)
```

The client can mark stale, refetch, or wait for a replacement snapshot according to subscription policy.

## Client Command Lifecycle

Command lifecycle:

```text
Pending
  command was accepted locally and overlay applied

Sent
  command was sent to the server

Accepted
  server accepted the command for execution

Committed
  authoritative module patch/snapshot was received

Rejected
  server rejected the command; overlay is removed

TimedOut
  command did not complete within policy; overlay may remain, warn, or roll back
```

The client should expose command status so UI can show saving, failure, retry, or conflict states.

## Conflict Handling

Commands should support optimistic concurrency.

Possible tokens:

- module sequence
- node version
- row version
- provider commit sequence
- application ETag

If the expected token does not match, the server can:

- reject with conflict
- apply command against latest state if safe
- return current authoritative module fragment
- request client refetch

The default should be conservative. Silent last-write-wins is easy and usually wrong.

## UI Notification Granularity

The client store should notify by dependency, not by global state change.

Subscription levels:

- module subscription
- node subscription
- edge subscription
- selector subscription
- command-status subscription

Patch application should collect affected identities:

```text
Changed nodes:
  TaskCard:t_1

Changed fields:
  TaskCard:t_1.Title

Changed edges:
  ProjectHeader:p_42.tasks

Changed command ids:
  RenameTask:cmd_123
```

Then it should notify only subscribers whose dependency set intersects the changed set.

The first implementation can start with module-level notifications, but it should shape internal patch metadata so node/edge/selector notifications can be added without rewriting the patch model.

## Server Patch Publication

Server patch publication should be post-commit and batched.

For one transaction:

1. collect mutation impact records
2. group by affected module/subscription
3. classify impact using module matrices
4. combine compatible field/node/edge operations
5. fall back to invalidation where precision is missing
6. publish one ordered module message per affected subscription or module scope

Do not send one websocket message per changed field. That turns a coherent state update into unnecessary transport chatter and repeated UI work.

## Authorization Changes

Authorization can affect:

- whether a module is visible
- whether a node is visible
- whether a field is visible
- whether an edge is visible
- whether a command is allowed

If a mutation changes authorization-relevant data, module impact analysis must treat it as a potential visibility change.

Safe behavior:

- invalidate affected modules
- refetch under the current authorization context
- remove module graph state if authorization is revoked

Precise auth patches can come later. Data leaks are not a performance tradeoff.

## Diagnostics

Mutation and invalidation diagnostics should include:

- commands dispatched
- commands accepted/rejected/conflicted/timed out
- optimistic overlays applied/rolled back
- mutation impact records produced
- modules considered
- modules classified as no-effect
- field patches produced
- node patches produced
- edge patches produced
- fragment refetches requested
- full module invalidations produced
- fallback reasons
- UI notifications emitted by level
- subscribers skipped because their dependencies were unaffected

Without this, users will not know whether a write rerendered half the app because it had to or because the implementation was lazy.

## First Useful Slice

The first useful slice should be conservative but correctly shaped:

1. define command envelope and command status model
2. add optimistic overlay support for field-level local edits
3. execute commands on the server through application handlers
4. after commit, invalidate affected modules and send full replacement snapshots
5. apply replacement snapshots transactionally on the client
6. notify at module level
7. record diagnostics for command status and module invalidation

The second slice can add field and node patches:

1. generate or register a module impact matrix
2. classify changed columns into no-effect, field patch, node upsert/remove, or module invalidate
3. apply patches transactionally
4. notify node and edge subscribers

Edge patches, fragment refetch, aggregate deltas, and cross-module deduplication should wait until the simple path is boring.

## Open Questions

- Should command handlers live in DataLinq.Store.Server or remain purely application-owned?
- Should command contracts be generated from methods, records, or explicit registration?
- Should optimistic overlays support arbitrary module patches or only generated command-specific overlays?
- Should the first authoritative patch unit be field, node, edge, or full module?
- How should server impact analysis handle modules cached for many authorization contexts?
- Should the module impact matrix be generated at build time or built from runtime module descriptors?
- Should client selector dependencies be tracked automatically or declared explicitly at first?
- How should patch batching interact with UI render scheduling in Blazor versus JavaScript frameworks?
