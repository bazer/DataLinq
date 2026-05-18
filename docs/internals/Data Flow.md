# Data Flow

This page follows the main paths through DataLinq. It is intentionally higher-level than the source code, but it names the real subsystems so you can map the diagrams back to the implementation.

## One-Page Mental Model

```mermaid
flowchart LR
    Models["Source models"] --> Generator["Source generator"]
    DatabaseSchema["Database schema"] --> CLI["CLI / tools"]
    CLI --> Models

    Generator --> Generated["Generated database and model types"]
    Generated --> Metadata["Generated metadata draft"]
    Metadata --> Factory["MetadataDefinitionFactory"]
    Factory --> Frozen["Frozen runtime metadata"]

    Generated --> Runtime["Runtime API"]
    Frozen --> Runtime
    Runtime --> Query["Query pipeline"]
    Runtime --> Mutation["Mutation pipeline"]
    Runtime --> Cache["Cache and invalidation"]

    Query --> Provider["Provider"]
    Mutation --> Provider
    Provider --> Db[("Database")]
    Cache --> Runtime
```

The core loop is:

1. generate a strong model surface
2. build finalized metadata
3. execute reads and writes through that metadata
4. keep caches coherent around provider-key identity
5. report behavior through diagnostics

## Model Generation Flow

```mermaid
sequenceDiagram
    participant Dev as Developer
    participant CLI as datalinq generate models
    participant Provider as Provider metadata reader
    participant Models as Source model files
    participant Generator as Source generator
    participant Output as Generated model files

    Dev->>CLI: Run generate models
    CLI->>Provider: Read live schema
    Provider-->>CLI: DatabaseDefinition boundary
    CLI->>Models: Create or refresh abstract models
    Models->>Generator: Compile project
    Generator->>Generator: Parse models and attributes
    Generator->>Generator: Build typed metadata draft
    Generator->>Output: Emit immutable, mutable, metadata, keys, relations
```

Generation is not only a convenience step. The generated output carries runtime hooks that providers now require during normal startup.

## Provider Startup Flow

```mermaid
sequenceDiagram
    participant App as Application
    participant Db as Database<T>
    participant Generated as Generated TDatabase
    participant Factory as MetadataDefinitionFactory
    participant Provider as Provider
    participant Cache as DatabaseCache

    App->>Db: new MySqlDatabase<T>(connectionString)
    Db->>Generated: GetDataLinqGeneratedMetadata()
    Generated-->>Db: MetadataDatabaseDraft
    Db->>Factory: Build(draft)
    Factory-->>Db: Frozen DatabaseDefinition
    Db->>Generated: SetDataLinqGeneratedMetadata(metadata)
    Db->>Provider: Initialize provider with metadata
    Db->>Cache: Create table cache state
```

If the generated metadata hook is missing or invalid, startup should fail loudly. That failure is better than silently running with stale model assumptions.

## Query Execution Flow

```mermaid
flowchart TD
    A["db.Query().Employees<br/>.Where(...).OrderBy(...)"] --> B["Remotion parses expression tree"]
    B --> C["DataLinq validates supported shape"]
    C --> D["Build SQL for supported predicates, ordering, paging, scalar operators"]
    D --> E["Execute key/select SQL through provider"]
    E --> F{"Rows in cache?"}
    F -- "hit" --> G["Reuse immutable instance"]
    F -- "miss" --> H["Fetch missing row data"]
    H --> I["Materialize immutable instance"]
    I --> J["Store by provider key"]
    G --> K["Apply supported projection"]
    J --> K
    K --> L["Return result"]
```

The query pipeline is intentionally bounded. It supports documented predicates, ordering, paging, projections, scalar aggregates, one explicit inner join shape, and relation-backed existence predicates. Unsupported expression shapes are rejected rather than guessed.

## Direct Primary-Key Lookup

```mermaid
flowchart LR
    A["Generated Get(...)"] --> B["Normalize to provider key"]
    B --> C["TableCache.GetRow<TKey>"]
    C --> D{"RowStore<TKey> hit?"}
    D -- "yes" --> E["Return cached immutable"]
    D -- "no" --> F["Provider fetch by primary key"]
    F --> G["Materialize immutable"]
    G --> H["Store in RowStore<TKey>"]
    H --> E
```

Generated scalar keys use provider CLR values directly. Generated composite keys use generated `DataLinqPrimaryKey` structs. Dynamic `DataLinqKey` is a bridge for metadata-driven paths, not the preferred generated row-cache key.

## Relation Traversal Flow

```mermaid
flowchart TD
    A["department.Managers"] --> B["Generated relation property"]
    B --> C["Read relation handle and provider foreign key"]
    C --> D{"Relation index cached?"}
    D -- "yes" --> E["Load related primary keys from index"]
    D -- "no" --> F["Query provider for relation keys"]
    F --> G["Populate relation index"]
    G --> E
    E --> H["Resolve target rows through table cache"]
    H --> I["Return immutable relation collection"]
```

Relation traversal is lazy and cache-aware. That is why relation/index invalidation is part of the cache design, not an afterthought.

## Mutation And Transaction Flow

```mermaid
sequenceDiagram
    participant App as Application
    participant Immutable as Immutable model
    participant Mutable as Mutable wrapper
    participant Tx as Transaction
    participant Provider as Provider SQL
    participant Cache as Cache state

    App->>Immutable: Mutate(...)
    Immutable-->>Mutable: Mutable copy
    App->>Mutable: Change properties
    Mutable->>Tx: Save / Insert / Update
    Tx->>Provider: Execute write command
    Provider-->>Tx: Persisted row/defaults
    Tx->>Cache: Apply state changes
    Cache-->>Tx: Invalidated or refreshed rows/indexes
    Tx-->>App: Fresh immutable instance
```

DataLinq does not rely on invisible dirty tracking. The mutation object is the write surface, and the transaction owns when changes become durable.

## Cache Invalidation Flow

```mermaid
flowchart TD
    A["Mutation, manual clear, or external event"] --> B["DatabaseCache facade"]
    B --> C{"Scope"}
    C -- "database" --> D["Clear database caches"]
    C -- "table" --> E["Clear table rows/indexes"]
    C -- "row / rows" --> F["Convert key components to provider keys"]
    F --> G["Remove typed row-store entries"]
    G --> H["Invalidate affected relation/index buckets"]
    D --> I["Record metrics"]
    E --> I
    H --> I
```

Precise invalidation uses provider-key values. When a signal cannot provide enough detail, DataLinq falls back to a conservative table/database clear.

## Schema Validation And Diff Flow

```mermaid
sequenceDiagram
    participant User as User
    participant CLI as datalinq validate / diff
    participant Source as Generated/source metadata
    participant Provider as Live provider metadata
    participant Compare as SchemaComparer
    participant Diff as SchemaDiffScriptGenerator

    User->>CLI: validate or diff
    CLI->>Source: Load model metadata
    CLI->>Provider: Read live schema metadata
    Source-->>Compare: Model DatabaseDefinition
    Provider-->>Compare: Database DatabaseDefinition
    Compare-->>CLI: Supported-boundary differences
    alt diff command
        CLI->>Diff: Generate conservative SQL suggestions
        Diff-->>User: SQL plus manual-review comments
    else validate command
        CLI-->>User: Text or JSON drift report
    end
```

Validation and diffing are schema trust tools. They depend on the provider metadata support matrix and intentionally avoid pretending to be full migration execution.

## Diagnostics Flow

```mermaid
flowchart LR
    Runtime["Runtime activity"] --> Metrics["DataLinqMetrics"]
    Query["Provider commands"] --> Metrics
    Cache["Row/cache/index activity"] --> Metrics
    Invalidation["Cache invalidation"] --> Metrics
    Metrics --> Snapshot["In-process snapshot"]
    Metrics --> Telemetry["System.Diagnostics.Metrics"]
```

The metrics model is hierarchical:

- runtime totals
- provider-instance metrics
- table-level cache and relation metrics

That shape avoids flattening different provider instances or table caches into one misleading number.
