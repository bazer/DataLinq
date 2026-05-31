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

This is the best starting direction.

## Recommended Authoring Model

Use a hybrid model:

> Module contract shape is declared with attributed C# types and properties. Data sources, edge policies, and graph construction are declared through a constrained module builder in a partial configuration method.

That gives us:

- DataLinq-like model authoring
- readable module classes
- analyzable properties and attributes
- functional graph configuration
- space for imperative helper code outside the contract-critical path

The first implementation should support a small subset:

- module class
- module parameters
- node/root properties
- edge properties
- module-level authorization
- eager module root
- paged/lazy edge policy metadata
- one command
- one client action
- source-generated serializers
- generated C# and TypeScript shape

## Supported Contract Subset

The generator should accept:

- attributed module classes
- records/classes for node state
- public get/init node fields
- supported scalar field types
- supported key types
- explicit module version
- explicit module-level authorization
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
  Nodes[]
  Edges[]
  SerializationShape
  CompatibilityHash

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
- query helper cannot be analyzed
- TypeScript name collision
- unsupported serializer shape

The generator should fail early. A module that cannot be represented in the IR should not silently fall back to runtime reflection.

## Open Questions

- Should the module contract type be a class, record, static partial class, or a pair of contract and factory types?
- Should module parameter types be separate records or properties on the module contract type?
- Should node types be nested inside the module for readability, or separate reusable records?
- Should edge policy be mostly attributes, mostly fluent builder calls, or a mix?
- Should the IR be emitted as a generated JSON manifest for TS/tooling, or stay internal to the generator pipeline?
- Should TypeScript generation run directly from the C# source generator or from a Store CLI/MSBuild target that consumes the IR manifest?
- How strict should V1 be about query helper method analysis?
