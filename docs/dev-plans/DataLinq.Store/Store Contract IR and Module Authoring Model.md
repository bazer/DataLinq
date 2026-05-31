> [!WARNING]
> This document is incubating architecture material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store Contract IR And Module Authoring Model

**Status:** Draft specification.

## Purpose

DataLinq.Store needs one central contract model that every generator, runtime adapter, and protocol feature consumes.

The core rule is:

> Parse developer-authored Store contracts once into a single Store Contract IR, then generate server adapters, C# clients, WebAssembly exports, TypeScript bindings, serializers, tests, diagnostics, module descriptors, and sync metadata from that IR.

Without this, the project risks becoming five semi-independent systems that each rediscover the meaning of modules, commands, edges, keys, authorization, and serialization.

## What Store Contract IR Means

IR means intermediate representation. It is not .NET IL. It is the normalized contract descriptor produced by the Store generator/analyzer from user-authored C#.

Conceptually:

```text
Developer C# authoring model
  -> Roslyn semantic analysis
  -> StoreContract IR
  -> generated outputs
```

The IR is the single product truth for:

- modules
- module parameters
- module versions
- nodes
- node fields
- edges
- edge policies
- commands
- client actions
- key policies
- authorization policies
- serialization shape
- protocol metadata
- generated TypeScript shape
- compatibility hashes
- diagnostics

Every generated artifact should be traceable back to the same IR.

## Why The IR Matters

The IR prevents drift.

Bad architecture:

```text
Module generator parses attributes one way.
ASP.NET adapter generator parses methods another way.
TypeScript generator parses public records another way.
WASM facade generator parses exports another way.
Runtime diagnostics reconstruct metadata another way.
```

That will eventually produce mismatched contracts.

Better architecture:

```text
StoreContract IR
  -> module descriptors
  -> serializers
  -> server adapters
  -> C# client proxy
  -> WASM export facade
  -> JS/TS wrappers
  -> test helpers
  -> diagnostics
```

If the IR cannot describe a feature, the feature is not in the supported Store contract yet.

## Authoring Goals

The module authoring model should be:

- easy to read
- easy to write
- close to DataLinq's table/model authoring style
- efficient for projection-heavy modules
- declarative where shape matters
- functional where graph composition matters
- imperative where application code genuinely needs it
- analyzable by source generators
- AOT-friendly
- clear when unsupported code appears

The authoring model should not require developers to write protocol DTOs, TypeScript declarations, serializer manifests, or endpoint adapters by hand.

## Hard Boundary: Contract Phase Versus Execution Phase

The design needs a clear split:

```text
Contract phase
  Analyzable. Defines modules, nodes, fields, edges, commands, versions, keys, auth, serialization.

Execution phase
  Application code. Executes command handlers, queries DataLinq, computes values, runs custom client actions.
```

Arbitrary imperative code is fine in execution. It is not fine when defining the contract-critical graph shape unless the generator can analyze it.

This should be a generator diagnostic, not a runtime surprise.

## Option A: Attribute/Property Module Classes

This is closest to DataLinq table authoring.

Conceptual shape:

```csharp
[StateModule("ProjectWorkspace", Version = 3)]
[AuthorizeModule(Policy = "CanViewProject")]
public sealed partial class ProjectWorkspace
{
    [ModuleParameter]
    public required ProjectId ProjectId { get; init; }

    [StateNode]
    public ProjectHeaderState Project { get; init; } = default!;

    [StateEdge(PageSize = 100)]
    public IReadOnlyList<TaskCardState> Tasks { get; init; } = [];

    [StateEdge(Load = EdgeLoad.Lazy)]
    public IReadOnlyList<AuditEntryState> AuditLog { get; init; } = [];
}

public sealed record TaskCardState(
    TaskRef Id,
    string Title,
    TaskStatus Status,
    UserRef? AssigneeId);
```

Pros:

- familiar to existing DataLinq users
- easy to inspect
- good static shape
- good TypeScript generation target
- good serializer generation target
- easy to diff in code review

Cons:

- does not by itself say how data is loaded
- relation/edge policies can become attribute soup
- complex module construction still needs another mechanism
- purely declarative shape may feel disconnected from DataLinq queries

Verdict:

Use this for contract shape where possible. It is good at defining the module graph schema.

## Option B: Fluent Module Factory

This is close to the examples already sketched.

Conceptual shape:

```csharp
[StateModule("ProjectWorkspace", Version = 3)]
[AuthorizeModule(Policy = "CanViewProject")]
public static partial class ProjectWorkspaceModule
{
    public static StateModule<ProjectWorkspaceParams> Define(
        StateModuleBuilder builder,
        ProjectDb db,
        ProjectWorkspaceParams parameters)
    {
        var project = builder.Node(
            "project",
            ProjectQueries.ProjectHeader(db, parameters.ProjectId));

        var tasks = builder.Collection(
            "tasks",
            ProjectQueries.TaskCards(db, parameters.ProjectId),
            edge => edge.Paged(pageSize: 100).PrefetchFirstPage());

        builder.Edge(project, tasks, "tasks");

        return builder.Build();
    }
}
```

Pros:

- expressive
- functional feel
- data sources and graph edges sit together
- good for paging/laziness/edge policy
- natural place for helper methods

Cons:

- arbitrary C# can become unanalyzable
- easy to hide dynamic behavior in helper methods
- harder to generate TypeScript shape unless node types are explicit
- requires a supported builder subset

Verdict:

Use this for graph composition and data binding, but keep it constrained and analyzable.

## Option C: Self-Declaring Partial Module Type

This combines declarative shape with a configure/factory method.

Conceptual shape:

```csharp
[StateModule("ProjectWorkspace", Version = 3)]
[AuthorizeModule(Policy = "CanViewProject")]
public sealed partial class ProjectWorkspaceModule
{
    [ModuleParameter]
    public required ProjectId ProjectId { get; init; }

    [StateRoot]
    public ProjectHeaderState Project { get; private init; } = default!;

    [StateEdge(PageSize = 100, Prefetch = EdgePrefetch.FirstPage)]
    public IReadOnlyList<TaskCardState> Tasks { get; private init; } = [];

    static partial void Configure(StateModuleBuilder<ProjectWorkspaceModule> module)
    {
        module.Root(x => x.Project)
            .From((db, p) => ProjectQueries.ProjectHeader(db, p.ProjectId));

        module.Edge(x => x.Tasks)
            .From((db, p) => ProjectQueries.TaskCards(db, p.ProjectId))
            .Paged(pageSize: 100)
            .PrefetchFirstPage();
    }
}
```

Pros:

- closest to DataLinq authoring philosophy
- module contract is readable as a type
- builder ties properties to data sources
- source generator can emit partial helpers
- good balance of declarative shape and functional configuration

Cons:

- partial/static configuration pattern needs careful design
- generator diagnostics need to be excellent
- still requires a supported configuration subset

Verdict:

This is the best starting direction, but it cannot be only shape-first. Query projection needs to be a first-class path inside this model, otherwise ordinary "this module is mostly this projected query" modules become awkward and repetitive.

## Recommended Authoring Model

Use a hybrid model:

> Module contract shape is declared with attributed C# types and properties. Data sources, edge policies, and graph construction are declared through a constrained module builder in a partial configuration method.

That gives us:

- DataLinq-like model authoring
- readable module classes
- analyzable properties and attributes
- functional graph configuration
- space for imperative helper code outside the contract-critical path

The hybrid model should support three authoring modes under one IR:

1. shape-first module authoring for modules with multiple roots, explicit edges, and mixed data sources
2. projection-first root or collection authoring for modules that are mostly one DataLinq query projection
3. graph-row projection authoring for later modules where one query produces several node types and edges

The first implementation should support a small subset:

- module class
- module parameters
- node/root properties
- edge properties
- projection-first root or collection source
- module-level authorization
- eager module root
- paged/lazy edge policy metadata
- one command
- one client action
- source-generated serializers
- generated C# and TypeScript shape

## Projection-First Roots And Collections

Projection-heavy modules must not require per-field hand mapping in the module definition.

The clean rule is:

> A projected node type can be the contract shape. The module binds a root or collection to a query that materializes that projected node type.

That keeps field declaration in one place: the projected node record or class.

Conceptual shape:

```csharp
[StateNode]
public sealed record TaskCard(
    [property: StateKey] TaskRef Id,
    string Title,
    TaskStatus Status,
    UserRef? AssigneeId,
    int SortOrder);

public static class TaskCardProjections
{
    public static readonly Expression<Func<TaskRow, TaskCard>> FromTask =
        task => new TaskCard(
            new TaskRef(task.Id),
            task.Title,
            task.Status,
            task.AssigneeId == null ? null : new UserRef(task.AssigneeId.Value),
            task.SortOrder);
}
```

The module should then bind a module property to the query projection:

```csharp
[StateModule("ProjectTaskBoard", Version = 1)]
[AuthorizeModule(Policy = "CanViewProject")]
public sealed partial class ProjectTaskBoardModule
{
    [ModuleParameter]
    public required ProjectId ProjectId { get; init; }

    [StateCollection(PageSize = 100)]
    public StateList<TaskCard> Tasks { get; private init; } = default!;

    static partial void Configure(StateModuleBuilder<ProjectTaskBoardModule> module)
    {
        module.Collection(x => x.Tasks)
            .From((ProjectDb db, ProjectTaskBoardModule p) =>
                db.Tasks
                    .Where(task => task.ProjectId == p.ProjectId)
                    .OrderBy(task => task.SortOrder)
                    .Select(TaskCardProjections.FromTask));
    }
}
```

The generator should derive the node fields, serializer shape, TypeScript shape, and compatibility hash from `TaskCard`. It should not require the module author to repeat `Title`, `Status`, `AssigneeId`, and `SortOrder` in the module declaration.

This also fits single-root modules:

```csharp
[StateRoot]
public ProjectHeader Header { get; private init; } = default!;

static partial void Configure(StateModuleBuilder<ProjectSummaryModule> module)
{
    module.Root(x => x.Header)
        .From((ProjectDb db, ProjectSummaryModule p) =>
            db.Projects
                .Where(project => project.Id == p.ProjectId)
                .Select(ProjectHeaderProjections.FromProject));
}
```

## Projection-Only Module Sugar

For the simplest case, where the module is intentionally just one query result, the API may support a shorter projection-only syntax.

Conceptual shape:

```csharp
[StateModule("ProjectTasks", Version = 1)]
[AuthorizeModule(Policy = "CanViewProject")]
public static partial class ProjectTasksModule
{
    [StateProjection(PageSize = 100)]
    public static IQueryable<TaskCard> Query(ProjectDb db, ProjectId projectId) =>
        db.Tasks
            .Where(task => task.ProjectId == projectId)
            .OrderBy(task => task.SortOrder)
            .Select(TaskCardProjections.FromTask);
}
```

This should be treated as syntax sugar over the same IR shape:

```text
Module
  Parameters: ProjectId
  Collection: TaskCard[]
  Source: ProjectTasksModule.Query
  Node: TaskCard
```

Do not make this a separate architecture. It is only a concise authoring form for one-root or one-collection modules.

## Graph-Row Projection

Some modules will naturally come from one query that produces several node types.

Example shape:

```csharp
public sealed record TaskBoardRow(
    TaskCard Task,
    UserChip? Assignee);
```

The authoring model should not infer an entire graph from arbitrary nested object initializers. That sounds convenient, but it is too magical for authorization, patching, generated TypeScript, and diagnostics.

Prefer explicit graph extraction from a projected row:

```csharp
module.Graph()
    .From((ProjectDb db, ProjectTaskBoardModule p) =>
        from task in db.Tasks
        join user in db.Users on task.AssigneeId equals user.Id into assignees
        from user in assignees.DefaultIfEmpty()
        where task.ProjectId == p.ProjectId
        select new TaskBoardRow(
            new TaskCard(
                new TaskRef(task.Id),
                task.Title,
                task.Status,
                task.AssigneeId == null ? null : new UserRef(task.AssigneeId.Value),
                task.SortOrder),
            user == null
                ? null
                : new UserChip(new UserRef(user.Id), user.DisplayName, user.AvatarUrl)))
    .Node(row => row.Task)
    .Node(row => row.Assignee)
    .Edge(row => row.Task, row => row.Assignee, edge => edge.Named("assignee"));
```

This is more verbose than implicit graph inference, but it is honest. It gives the generator stable node types and explicit edges while still letting one query feed the whole module graph.

Graph-row projection should probably not be in the first implementation unless the first demo genuinely needs it. Projection-first single root or collection authoring is more important.

## Projection Authoring Rules

Projection-first support should follow these rules:

- the projected node type defines the public contract fields
- the module property defines whether the projection is a root, collection, edge, page, or window
- the query defines how the server materializes the projected node type
- the Store Contract IR records both the contract type and the source query binding
- generated serializers and TypeScript declarations come from the projected node type
- dependency tracking comes from the DataLinq query execution path, not from client-visible metadata
- anonymous projection types are allowed internally only if they are not public Store contract shapes
- graph edges are explicit unless the author uses a deliberately supported convention

The practical test is simple: if a developer already has a clean DataLinq projection to a record type, turning it into a Store module should require almost no extra mapping.

## Supported Contract Subset

The generator should accept:

- attributed module classes
- records/classes for node state
- public get/init node fields
- supported scalar field types
- supported key types
- explicit module version
- explicit module-level authorization
- projection-first root and collection bindings
- projection expressions or query helper methods returning known projected node types
- builder calls with stable property selectors
- builder calls to known edge policy methods
- references to known query/helper methods when marked as Store-compatible

The generator should reject or warn on:

- dynamic module ids
- dynamic node type shape
- dynamic edge names
- reflection-based contract construction
- loops that create contract members dynamically
- conditionally included fields
- field-level authorization
- node-level authorization
- edge-level authorization
- anonymous node types in public module contracts
- implicit graph inference from arbitrary nested projection shapes
- unsupported serializer shapes

## Imperative Code Policy

Imperative code is allowed in execution paths:

- command handlers
- query helper methods
- custom client actions
- selector logic over loaded graph state
- validation helpers

Imperative code is restricted in contract-definition paths. A module definition cannot depend on runtime conditions to decide which fields or edges exist.

Allowed:

```csharp
module.Edge(x => x.Tasks)
    .From((db, p) => ProjectQueries.TaskCards(db, p.ProjectId))
    .Paged(100);
```

Not allowed:

```csharp
if (DateTime.Now.DayOfWeek == DayOfWeek.Monday)
{
    module.Edge("mondayOnly", ...);
}
```

The module shape must be stable for versioning, TypeScript generation, serialization, and compatibility.

## IR Shape

The first Store Contract IR can be modeled conceptually as:

```text
StoreContract
  ProtocolVersion
  GeneratorVersion
  SchemaHash
  Modules[]
  Commands[]
  ClientActions[]

ModuleContract
  ModuleId
  Version
  Parameters[]
  AuthorizationPolicy
  KeyPolicy
  Sources[]
  Nodes[]
  Edges[]
  SerializationShape
  CompatibilityHash

SourceContract
  SourceId
  TargetKind
  TargetMember
  QueryMethod
  ProjectionType
  DependencyInputs

NodeContract
  NodeTypeId
  ClrType
  TypeScriptName
  Key
  Fields[]

EdgeContract
  EdgeId
  SourceNode
  TargetNode
  Cardinality
  LoadPolicy
  PagingPolicy
  SortPolicy
  RetentionPolicy

CommandContract
  CommandId
  Version
  RequestType
  ResponseType
  AuthorizationPolicy
  ExpectedVersionPolicy

ClientActionContract
  ActionId
  ExportName
  Parameters[]
  ReturnType
```

The real implementation can choose records/classes/JSON manifests, but the conceptual shape should stay stable.

## Generated Outputs From IR

The IR should feed:

- module descriptors
- module snapshot serializers
- module patch serializers
- ASP.NET endpoint adapters
- C# client proxy
- WebAssembly export facade
- TypeScript declaration files
- JavaScript wrappers
- test helpers
- diagnostics/debug dumps
- protocol compatibility hashes
- AOT/trimming metadata

No output should independently rediscover module shape from arbitrary source symbols.

## Diagnostics

Generator diagnostics should be specific:

- unsupported dynamic edge name
- unsupported field type
- missing module version
- missing module authorization
- field-level authorization rejected
- module shape depends on runtime branch
- projection target cannot be represented as a Store node
- query helper cannot be analyzed
- graph-row projection is missing explicit node or edge extraction
- TypeScript name collision
- unsupported serializer shape

The generator should fail early. A module that cannot be represented in the IR should not silently fall back to runtime reflection.

## Open Questions

- Should the module contract type be a class, record, static partial class, or a pair of contract and factory types?
- Should module parameter types be separate records or properties on the module contract type?
- Should node types be nested inside the module for readability, or separate reusable records?
- Should edge policy be mostly attributes, mostly fluent builder calls, or a mix?
- Should projection-only modules be supported in V1 or only after the self-declaring partial module form works?
- Should graph-row projections be a V1 feature or a deliberate V2 feature after single projection roots and collections are proven?
- Should the IR be emitted as a generated JSON manifest for TS/tooling, or stay internal to the generator pipeline?
- Should TypeScript generation run directly from the C# source generator or from a Store CLI/MSBuild target that consumes the IR manifest?
- How strict should V1 be about query helper method analysis?
