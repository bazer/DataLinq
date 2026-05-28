> [!WARNING]
> This document is incubating architecture material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store Identity, Versioning, And Protocol Compatibility

**Status:** Draft specification.

## Purpose

DataLinq.Store needs boring identity and version rules before sync, caching, generated bindings, optimistic updates, and persistence can be reliable.

The core rule is:

> Every snapshot, patch, command, subscription, and generated binding must identify the contract and sequence it belongs to.

If the client and server disagree about module shape, command shape, schema version, or patch base sequence, the system must fail explicitly.

## Identity Surfaces

The system needs stable identifiers for:

- database
- tenant or logical data scope
- module
- module version
- module parameter set
- node type
- node key
- edge name
- command
- command version
- subscription
- snapshot sequence
- patch sequence
- generated schema
- Store protocol

These identifiers should appear in diagnostics and protocol messages.

## Module Identity

Module identity is not only the module name.

A module instance is identified by:

```text
DatabaseId
ModuleId
ModuleVersion
ParameterHash
AuthorizationContextStamp
SchemaHash
```

Depending on deployment, tenant/session can also be part of identity.

Two module snapshots with different authorization context stamps must not be treated as interchangeable unless the module explicitly proves authorization does not affect shape.

## Parameter Hashing

Parameter hash rules must be deterministic.

Requirements:

- stable property ordering
- stable numeric and date formatting
- stable string normalization rules
- no culture-sensitive serialization
- explicit handling of nulls
- versioned hash algorithm

The raw parameter payload may be logged only according to application privacy policy. The hash should be enough for cache keys and diagnostics in most cases.

## Node Identity

Node identity is scoped by:

```text
Module instance
NodeType
NodeKey
```

The first implementation should prefer module-scoped graph storage. Cross-module node deduplication is tempting but risky because contract shape, authorization ownership, and key policy may differ across modules.

Cross-module deduplication can be added later when the system can prove:

- same node type contract
- same node version
- same key policy
- compatible authorization ownership

## Edge Identity

Edge identity is scoped by:

```text
Module instance
SourceNodeKey
EdgeName
```

Paged or windowed edges also need:

- page/window key
- sort descriptor version
- filter descriptor version
- cursor/version token

Edge patches must identify the expected base sequence or edge version they apply to.

## Sequence Model

The first sequence model should be conservative.

Recommended fields:

```text
SnapshotSequence
PatchBaseSequence
PatchSequence
ServerCommitSequence
ClientAckSequence
```

Rules:

- a patch applies only to the expected base sequence
- duplicate patch sequence is ignored
- older patch sequence is ignored or rejected
- skipped patch sequence triggers replay or refetch
- a full replacement snapshot resets the module sequence

The sequence can be global, per connection, or per module. Per-module sequence is likely easiest for module patch correctness. Transport replay may still use a connection/global stream sequence.

## Generated Schema Hash

Generated clients and servers need schema compatibility metadata.

Include:

- Store protocol version
- generator version
- module ids and versions
- command ids and versions
- node type shapes
- edge shapes
- serializer format version
- hash algorithm version

The server should reject incompatible clients with a clear `SchemaMismatch` or `UnsupportedClientVersion` response. Silent compatibility drift is unacceptable.

## Protocol Compatibility

Protocol messages should be forward-compatible where safe.

Rules:

- unknown optional fields can be ignored
- unknown required fields reject the message
- unknown module id rejects subscription
- unknown command id rejects command
- unknown node type in a patch rejects or refetches the module
- unknown edge in a patch rejects or refetches the module
- unsupported protocol version rejects connection or downgrades only when explicitly supported

Do not invent automatic protocol downgrade unless a real compatibility matrix exists.

## Message Metadata

Every message should include:

```text
ProtocolVersion
SchemaHash
MessageId
CorrelationId
Timestamp or server tick
```

Module messages additionally include:

```text
DatabaseId
ModuleId
ModuleVersion
ParameterHash
AuthorizationContextStamp
SubscriptionId
Sequence
```

Command messages additionally include:

```text
CommandId
CommandVersion
ClientCommandId
ExpectedSequence or expected version
```

## Reconnect And Replay

Reconnect flow:

1. client sends known subscriptions and last acknowledged sequences
2. server decides whether replay is possible
3. if replay is possible, server sends missed patches
4. if replay is not possible, server sends replacement snapshots or invalidations
5. client applies transactionally and acknowledges

If the server cannot prove patch continuity, it must not guess. Replace or invalidate.

Server-side replay and refetch decisions are also constrained by [Server Subscription and Module Cache Architecture](Server%20Subscription%20and%20Module%20Cache%20Architecture.md): a subscribed client cannot assume the server retained its module snapshot.

## Browser Bundle And Server Drift

Normal web deployment can serve an old JS/WASM bundle while the server has moved on.

Required behavior:

- generated client includes schema hash
- server exposes compatible schema hash
- mismatch fails early
- app can instruct user to reload
- persisted modules are invalidated when schema hash changes

This is not optional. Stale browser bundles are ordinary web reality.

## Golden Compatibility Tests

Compatibility tests should exist early.

Minimum cases:

- snapshot payload hydrates with matching schema
- snapshot payload with unknown optional field hydrates
- snapshot payload with unknown required field fails
- patch with wrong base sequence fails
- duplicate patch is ignored
- skipped patch triggers refetch
- old client schema is rejected
- command with old command version is rejected or mapped explicitly
- unknown module id is rejected
- persisted module with old schema hash is rejected

These tests should run without a browser first. Browser smoke tests should cover generated JS/WASM binding compatibility after the core contract tests pass.

## Diagnostics

Diagnostics should expose:

- schema hash
- protocol version
- generated client version
- generated server version
- active module identities
- subscription ids
- last snapshot sequence per module
- last acknowledged sequence per module
- patch rejections by reason
- reconnect replay/refetch decisions
- persisted state rejected by schema/version mismatch

## Open Questions

- Should module sequence be per module instance, per subscription, or per server stream?
- Should client command ids be generated by the Store runtime or supplied by application code?
- Should schema hash include generated TypeScript output or only the Store contract model?
- Should version compatibility be exact-match only in the first implementation?
- Should persisted state be deleted or quarantined after schema mismatch?
- How should module parameter hashes be made explainable in diagnostics without logging sensitive parameters?
