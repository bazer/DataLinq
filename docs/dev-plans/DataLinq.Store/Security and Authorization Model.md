> [!WARNING]
> This document is incubating architecture material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store Security And Authorization Model

**Status:** Draft specification.

## Purpose

Authorization is a central module concept, not a wrapper around endpoints.

The hard rule is:

> Authorization applies to the whole module instance. If the user is authorized, they can receive the complete module contract. If not, they receive none of it.

DataLinq.Store will not support field-level, node-level, or edge-level authorization inside one module contract. Different visibility must be modeled as different modules or different module contracts.

Generated bindings, TypeScript types, WebAssembly exports, optimistic overlays, and client-side C# are ergonomics. They are not security boundaries. The server must validate every command, module subscription, parameter set, patch, refetch, and persisted-state hydration decision.

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

Authorization applies at these boundaries:

- API access
- module subscription
- module parameters
- command execution
- command parameters
- optimistic overlay acceptance
- module snapshot delivery
- module patch delivery
- module refetch
- persisted client state hydration

Endpoint-level authorization is necessary but insufficient. A user authorized to call the Store sync endpoint may only be authorized for some module ids and some parameter values.

## Module Authorization Contract

Every module should declare or register an authorization contract.

The contract should answer:

- who can subscribe to this module?
- which parameter values are allowed?
- what authorization context stamp should identify this module instance?
- what should happen when authorization changes?

Conceptual sketch:

```csharp
[StateModule("ProjectWorkspace", Version = 3)]
[AuthorizeModule(Policy = "CanViewProject")]
public static StateModule<ProjectWorkspaceParams> ProjectWorkspace(...)
```

The syntax is open. The requirement is not: authorization is part of the module contract and affects cache keys, snapshots, patches, persistence, and diagnostics.

## No Field-Level Authorization

One module contract has one visibility shape.

Do not put privileged and non-privileged state in the same module and try to hide parts of it per user.

Bad:

```text
ProjectWorkspace
  TaskCard:
    Title
    Status
    InternalCost
    SecurityLabel
```

with `InternalCost` and `SecurityLabel` hidden for some users.

Good:

```text
ProjectWorkspace
  TaskCard:
    Title
    Status

ProjectWorkspaceAdmin
  TaskCardAdmin:
    Title
    Status
    InternalCost
    SecurityLabel
```

If users see different state, define different modules. This keeps generated TypeScript, serialized snapshots, patches, client persistence, cache keys, and tests honest.

## Edge And Count Leakage

Because authorization is module-level, the module author must avoid exposing edges that leak information to unauthorized users.

Examples:

- a hidden `AuditEntries` edge reveals that audit entries exist
- a count-only edge reveals hidden row counts
- edge ordering reveals priority or security classification
- a missing node reveals deletion or lack of access

If this information is sensitive, put it behind a separate authorized module.

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

Module cache keys must include authorization context whenever authorization affects module visibility.

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

The exact stamp should be application-configurable. The dangerous case is using a module snapshot generated for one authorization context as the cached result for a different context. If there is doubt, isolate the cache entry by user/session or do not cache that module.

## Authorization Changes

Authorization can change because:

- user role changed
- membership changed
- object ACL changed
- tenant boundary changed
- command changed ownership or visibility data
- external system updated permissions
- session expired

Safe behavior:

```text
Authorization change detected
  -> invalidate affected modules
  -> remove unauthorized graph state
  -> require refetch under current authorization
```

Precise authorization patches are out of scope. Authorization changes invalidate or clear whole module instances.

## Commands

Every command must be authorized server-side.

The server should check:

- can the user execute this command?
- are the command parameters valid for this user?
- is the target module visible enough for this command?
- is the command allowed in the current state?
- does the expected version/sequence still match?

Client-side validation can provide fast feedback. Server-side validation decides.

## Patches

Patches must obey the same module authorization rules as snapshots.

A patch cannot include:

- a module the user is no longer authorized to see
- an internal provider key when opaque keys are required
- a node or edge that is outside the module contract

If a mutation affects authorization-sensitive data, the safe default is module invalidation and refetch.

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
- generated client APIs expose module contracts, not database rows
- generated TypeScript types reflect module contracts
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
- schema/client mismatch rejections
- attempts to call unknown module or command ids
- stale persisted state rejected on hydration

Diagnostics must not leak sensitive values while trying to explain security decisions.

## Testing Strategy

Minimum tests:

- unauthorized module subscription is rejected
- unauthorized parameter value is rejected
- module snapshot includes only the declared module contract
- module patch includes only the declared module contract
- authorization change invalidates or clears affected module state
- admin and non-admin modules use separate module contracts or separate authorization cache identities
- command authorization is enforced server-side even when client validation is bypassed
- persisted state is rejected after authorization context mismatch
- opaque keys prevent provider-key leakage where configured

These should run as normal .NET tests where possible, with browser smoke tests for persistence and binding-specific behavior.

## Open Questions

- How should applications declare authorization context stamps without exploding module cache cardinality?
- Should persisted sensitive modules be opt-in or opt-out?
- Should opaque keys be default for browser modules?
- How should authorization diagnostics explain rejected graph state without leaking it?
- Should `AuthorizationRevoked` clear the whole module immediately or mark stale until refetch confirms visibility?
