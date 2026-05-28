> [!WARNING]
> This document is incubating architecture material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store Security And Authorization Model

**Status:** Draft specification.

## Purpose

Authorization is a central module concept, not a wrapper around endpoints.

The hard rule is:

> A client only receives module graph state it is authorized to see at that moment, and every patch obeys the same authorization rules as the original snapshot.

Generated bindings, TypeScript types, WebAssembly exports, optimistic overlays, and client-side C# are ergonomics. They are not security boundaries. The server must validate every command, module subscription, parameter set, field, edge, patch, and refetch.

## Threat Model

Assume the client is hostile.

A user can:

- modify JavaScript
- call generated WebAssembly exports directly
- call generated HTTP/WebSocket endpoints directly
- replay old command payloads
- change module parameters
- attempt to subscribe to modules they should not see
- inspect downloaded WebAssembly and generated TypeScript
- tamper with IndexedDB/local storage
- run multiple tabs or stale client bundles

Therefore, no client-side check is authoritative.

## Authorization Surfaces

Authorization applies at multiple levels:

- API access
- module subscription
- module parameters
- node existence
- node fields
- edges
- edge items
- commands
- command fields
- optimistic overlay acceptance
- patches
- persisted client state hydration

Endpoint-level authorization is necessary but insufficient. A user authorized to call `ProjectWorkspace` may only be authorized for one `ProjectId`, may only see some task nodes, and may only see selected fields.

## Module Authorization Contract

Every module should declare or register an authorization contract.

The contract should answer:

- who can subscribe to this module?
- which parameter values are allowed?
- which node types are visible?
- which node instances are visible?
- which fields are visible?
- which edges are visible?
- which edge items are visible?
- what should happen when authorization changes?

Conceptual sketch:

```csharp
[StateModule("ProjectWorkspace", Version = 3)]
[AuthorizeModule(Policy = "CanViewProject")]
public static StateModule<ProjectWorkspaceParams> ProjectWorkspace(...)
```

The syntax is open. The requirement is not: authorization is part of the module contract and affects cache keys, snapshots, patches, and diagnostics.

## Field And Edge Security

Do not ship hidden fields for future convenience.

Bad:

```text
TaskCard {
  Title,
  Status,
  InternalCost,
  SecurityLabel
}
```

with UI code hiding `InternalCost`.

Good:

```text
TaskCard {
  Title,
  Status
}
```

with a separate privileged module or field policy when privileged users need more state.

Edges can leak too. The existence, count, order, or absence of an edge item can reveal sensitive information. Authorization must apply to edge membership, not only node fields.

## Client Keys And Identifier Leakage

Provider primary keys are not always safe to expose.

Key modes:

- provider key visible as client key
- opaque deterministic client key
- session-scoped opaque key
- module-scoped synthetic key

Opaque keys are useful when real identifiers leak:

- tenant shape
- row counts
- creation ordering
- internal topology
- cross-system joins

The server owns key mapping. The client only needs stable identity within the authorized module/session and enough information to apply patches.

## Authorization Context And Cache Keys

Module cache keys must include authorization when authorization affects shape.

Cache key components may include:

- module id
- module version
- parameter hash
- schema hash
- tenant id
- user id
- role/policy stamp
- permission version
- data-visibility scope

The exact stamp should be application-configurable. The dangerous case is using a module snapshot generated for an admin as the cached result for a non-admin. If there is doubt, isolate the cache entry by user/session or do not cache that module.

## Authorization Changes

Authorization can change because:

- user role changed
- membership changed
- object ACL changed
- tenant boundary changed
- command changed ownership or visibility fields
- external system updated permissions
- session expired

Safe behavior:

```text
Authorization change detected
  -> invalidate affected modules
  -> remove unauthorized graph state
  -> require refetch under current authorization
```

Precise authorization patches are possible later, but the first implementation should favor invalidation/refetch. Data leaks are not an optimization problem.

## Commands

Every command must be authorized server-side.

The server should check:

- can the user execute this command?
- are the command parameters valid for this user?
- is the target node/module visible enough for this command?
- is the command allowed in the current state?
- does the expected version/sequence still match?

Client-side validation can provide fast feedback. Server-side validation decides.

## Patches

Patches must obey the same authorization rules as snapshots.

A patch cannot include:

- a hidden field
- a hidden node
- a hidden edge
- an unauthorized edge item
- an internal provider key when opaque keys are required

If a mutation affects a module through authorization-sensitive data, the safe default is module invalidation and refetch.

## Client Persistence

Hydrated client state may outlive authorization.

Rules:

- persisted modules should carry authorization context metadata
- sensitive modules may opt out of persistence
- logout clears persisted state for the user/session
- authorization mismatch on startup marks persisted modules unusable
- server may require persisted modules to refetch before display

Do not assume IndexedDB is private enough for sensitive state. It is browser storage controlled by the client.

## Generated Bindings

Generated bindings should make secure use easy:

- generated server adapters call authorization hooks
- generated client APIs do not expose hidden fields
- generated TypeScript types reflect the authorized module contract shape, not database rows
- generated command methods include version/sequence parameters where needed
- generated WebAssembly exports stay coarse and do not expose internal provider operations

But generated bindings are not enough. The server still checks every request.

## Diagnostics And Auditing

Security diagnostics should include:

- module authorization checks
- denied module subscriptions
- denied commands
- authorization-context cache key components, redacted where needed
- authorization-driven invalidations
- hidden field/edge decisions in debug builds
- schema/client mismatch rejections
- attempts to call unknown module or command ids
- stale persisted state rejected on hydration

Diagnostics must not leak sensitive values while trying to explain security decisions.

## Testing Strategy

Minimum tests:

- unauthorized module subscription is rejected
- unauthorized parameter value is rejected
- hidden fields are absent from snapshots and patches
- hidden edges are absent from snapshots and patches
- authorization change invalidates or clears affected module state
- admin and non-admin module cache entries do not cross-contaminate
- command authorization is enforced server-side even when client validation is bypassed
- persisted state is rejected after authorization context mismatch
- opaque keys prevent provider-key leakage where configured

These should run as normal .NET tests where possible, with browser smoke tests for persistence and binding-specific behavior.

## Open Questions

- How should applications declare authorization context stamps without exploding module cache cardinality?
- Should field-level authorization be first-class, or should developers define separate module node types for privileged views?
- Should persisted sensitive modules be opt-in or opt-out?
- Should opaque keys be default for browser modules?
- How should authorization diagnostics explain hidden graph state without leaking it?
- Should `AuthorizationRevoked` clear the whole module immediately or mark stale until refetch confirms visibility?
