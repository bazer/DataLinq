> [!WARNING]
> This document is incubating architecture material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store State Modules And Graph Cache

**Status:** Draft specification.

## Purpose

State modules are the proposed shared primitive for DataLinq.Store and DataLinq's future result-cache work.

The core idea is:

> A state module is a developer-defined, versioned, queryable, syncable graph projection over DataLinq data.

The module is the thing the server can cache, validate, serialize, patch, and sync. The same module is the thing the client can hydrate, query locally, render, and update from server patches.

This is a better primitive than raw result sets because it lets the developer define exactly which state exists outside the server:

- which fields can leave the server
- which relations exist in client state
- which keys are visible to the client
- which dependencies make the module stale
- which shape the serializer can optimize
- which local queries the client can run

## Definition

A state module is not a table and not a database row. It is also more structured than an arbitrary DTO.

A module has:

- stable module identity
- module version
- parameter contract
- authorization policy
- node types
- node keys
- node fields
- edges between node types
- optional root nodes or entry points
- dependency fingerprint
- serializer contract
- patch and invalidation behavior
- queryable surface for server and client execution

Sketch:

```text
Module:
  Name: ProjectWorkspace
  Version: 3
  Parameters:
    ProjectId

Nodes:
  ProjectHeader
    Key: ProjectRef
    Fields: Name, UpdatedAt, UserPermission

  TaskCard
    Key: TaskRef
    Fields: Title, Status, AssigneeRef, SortOrder

  UserChip
    Key: UserRef
    Fields: DisplayName, AvatarUrl

Edges:
  ProjectHeader -> TaskCard[] as Tasks
  TaskCard -> UserChip? as Assignee
```

The module can be small, screen-sized, session-sized, or intentionally broad. If a developer wants one module to represent the whole client state, that should be supported. The important point is that the whole-state module is still explicit and versioned.

## Why Modules Instead Of Rows

Shipping full database rows to the client is usually the wrong default.

Problems with raw rows:

- they leak columns that are irrelevant or unauthorized
- they couple client state to persistence schema
- they make payload size worse
- they expose database primary keys even when opaque client identity would be safer
- they blur authorization boundaries
- they force the client to understand server storage details

State modules let the developer define a product/API contract over the database model. The database remains the source of truth. The module is the client-visible state contract.

## Node Types

A module node is a state object inside a module graph.

It may correspond closely to one database row, but that is not required. A node can be:

- a projection of one row
- an aggregate over several rows
- a denormalized display object
- a permission-aware view over a row
- a synthetic node created by application logic

Example:

```csharp
public sealed record TaskCardState(
    TaskRef Id,
    string Title,
    TaskStatus Status,
    UserRef? AssigneeId,
    int SortOrder);
```

The module should track which database rows and invalidation markers were used to build each node, but the client does not need to receive that internal dependency detail unless diagnostics require it.

## Keys

Modules should not require exposing provider primary keys directly.

Supported key modes should include:

- provider key visible as client key
- generated opaque client key
- session-scoped key mapping
- module-scoped synthetic key

Opaque keys matter when real database identifiers leak tenant shape, row counts, sequencing, or internal topology.

The server owns the mapping between provider keys and client keys. The client only needs stable identity within the module/session and enough information to apply patches.

## Edges

Edges define relationships between module node types. They are the client-visible graph.

An edge can represent:

- one-to-one relation
- optional relation
- ordered collection
- unordered collection
- grouped collection
- lookup by key

Edges should be explicit because they drive:

- serialization
- patch application
- local query planning
- dependency tracking
- invalidation precision
- UI subscription notifications

An edge is not automatically every DataLinq relation. It is only the relation the module author exposed.

## Module Authoring

The exact authoring syntax is open, but the shape should be explicit and generated-friendly.

Conceptual sketch:

```csharp
[StateModule("ProjectWorkspace", Version = 3)]
public static StateModule<ProjectWorkspaceParams> ProjectWorkspace(
    StateModuleBuilder builder,
    ProjectDb db,
    ProjectId projectId)
{
    var project = builder.Node(
        "project",
        db.Projects
            .Where(project => project.Id == projectId)
            .Select(project => new ProjectHeaderState(
                project.Id,
                project.Name,
                project.UpdatedAt)));

    var tasks = builder.Collection(
        "tasks",
        db.Tasks
            .Where(task => task.ProjectId == projectId)
            .OrderBy(task => task.SortOrder)
            .Select(task => new TaskCardState(
                task.Id,
                task.Title,
                task.Status,
                task.AssigneeId,
                task.SortOrder)));

    var users = builder.Collection(
        "visibleUsers",
        db.Users
            .WhereVisibleForProject(projectId)
            .Select(user => new UserChipState(
                user.Id,
                user.DisplayName,
                user.AvatarUrl)));

    builder.Edge(project, tasks, "tasks");
    builder.Edge(tasks, users, "assignee", task => task.AssigneeId, user => user.Id);

    return builder.Build();
}
```

This example is not an API commitment. It shows the requirements:

- named module
- versioned contract
- parameterized state
- projected fields
- explicit edges
- generated descriptor potential

## Server Module Cache

The server-side cache should cache module snapshots or validated module fragments, not arbitrary raw query results.

A module cache entry contains:

- module identity
- module version
- parameter hash
- authorization context hash or policy stamp when required
- dependency fingerprint
- serialized or materialized module snapshot
- freshness token
- last validated sequence
- size estimate

The cache can validate a module without reloading the full module when dependency markers prove it is still fresh.

If precision cannot be proven, the server invalidates the module and recomputes it.

## Client Module Store

The client stores module snapshots as graph state:

```text
Modules:
  ProjectWorkspace(project:42)
    Version: 3
    Sequence: 1024
    Root: ProjectHeader:p_42

Nodes:
  ProjectHeader:p_42
  TaskCard:t_1
  TaskCard:t_2
  UserChip:u_10

Edges:
  ProjectHeader:p_42.tasks => [TaskCard:t_1, TaskCard:t_2]
  TaskCard:t_1.assignee => UserChip:u_10
```

The client may internally normalize nodes across modules when it can prove identity and field compatibility. That should be an optimization, not the first correctness requirement.

The safe first model is module-scoped graph storage. Cross-module deduplication can come after module versioning, key policy, and authorization ownership are boring.

## Querying

Modules should be queryable on both server and client, but the execution semantics differ.

Server querying:

- can read the database
- can use DataLinq query translation
- can enforce authorization
- can build or validate module snapshots
- can decide whether to patch or invalidate

Client querying:

- queries only loaded module graph state
- uses generated indexes and descriptors where available
- cannot discover or fetch unauthorized server data
- can derive UI views and local selectors
- can query across loaded modules only when the module contracts allow it

This distinction must be clear in APIs and docs. A client query is not a remote database query unless it explicitly asks the server to subscribe or refetch.

## Serialization

Modules make efficient serialization realistic because their shape is known.

The first portable format can be compact JSON:

```json
{
  "module": "ProjectWorkspace",
  "version": 3,
  "sequence": "1024",
  "nodes": {
    "TaskCard": {
      "fields": ["id", "title", "status", "assigneeId", "sortOrder"],
      "rows": [
        ["t_1", "Fix login", "Active", "u_10", 10],
        ["t_2", "Write docs", "Done", null, 20]
      ]
    }
  },
  "edges": {
    "ProjectHeader:p_42.tasks": ["TaskCard:t_1", "TaskCard:t_2"]
  }
}
```

That avoids repeating property names per row while keeping the format inspectable. A binary format can come later for .NET-to-.NET or high-volume paths.

Generated serializers should be preferred for AOT and browser payload discipline.

## Patch Model

Minimum message concepts:

```text
ModuleSnapshot
  Full module graph for a module identity and parameter set.

ModulePatch
  Transactional node and edge changes against a known module sequence.

ModuleInvalidate
  Tells the client that a module cannot be proven fresh and must refetch or mark stale.
```

Patch operations should include:

- upsert node
- remove node
- set field
- replace edge
- insert edge item
- remove edge item
- move edge item
- mark module stale
- replace full module

The first implementation can use full replacement snapshots after invalidation. Incremental patches should be added only when dependency and ordering semantics are clear.

Mutation-driven patches are specified in [Mutation and Invalidation Loop](Mutation%20and%20Invalidation%20Loop.md). The important rule is that module patches should be as precise as the module impact analysis can prove, and no more precise than that.

## Security

Modules are a security boundary.

Rules:

- module definitions decide which fields can leave the server
- module definitions decide which relations can leave the server
- parameters must be validated and authorization-checked
- module snapshots must not include hidden fields for future convenience
- client keys can be opaque when provider keys are sensitive
- authorization revocation must invalidate affected modules
- patches must obey the same authorization rules as snapshots

The client must never be trusted to define arbitrary module shape.

## Relationship To Result-Set Caching

DataLinq's result-cache work should treat module snapshots as the main concrete result shape.

The generic result-cache mechanism still matters:

- tracking reads
- building dependency fingerprints
- validating freshness
- reporting diagnostics
- bounding cache size

State modules add the product/API contract:

- what result shape exists
- how it can be serialized
- how it can be patched
- how mutations impact it
- how the client can query it
- how authorization applies to it

In other words, result-set caching is the engine. State modules are the cacheable and syncable vehicle.

## First Useful Slice

The first useful slice should avoid overreach:

1. define module descriptor DTOs
2. define node and edge descriptor DTOs
3. define module snapshot DTOs
4. define full-module replacement on change
5. support one module over one generated test model
6. serialize compact JSON
7. hydrate in a client in-memory store
8. query loaded module nodes locally
9. dispatch one command to mutate server state
10. invalidate and refetch the module after the server mutation

No incremental patches, SQLite/OPFS, offline conflict resolution, CDC, or cross-module deduplication are required for the first proof.

## Open Questions

- Should module definitions live in DataLinq.Store attributes, fluent registration, generated partial classes, or application code conventions?
- Should module descriptors be generated at build time or constructed by runtime registration in the first proof?
- Should the server cache module snapshots as materialized objects, serialized bytes, or both?
- How should authorization context affect module cache keys without exploding cache cardinality?
- When can two modules safely share a node instance on the client?
- Should opaque client key mapping be deterministic, session-scoped, or module-scoped?
- How much local query capability should be generated for each module?
- Should the first patch model support field-level updates, or should full node replacement be the minimum incremental unit?
