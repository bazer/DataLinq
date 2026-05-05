# Warning Cleanup Plan

Audit date: 2026-05-05

This plan covers the warnings emitted by the current `src/DataLinq.sln` Debug build after a clean restore. The goal is not to hide warnings. The goal is to make the compiler agree with the invariants the code already relies on, and to change the invariants where the compiler is catching a real ambiguity.

## Audit Baseline

Commands used:

```powershell
.\scripts\dotnet-sandbox.ps1 restore src\DataLinq.sln -v minimal
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.sln -c Debug -v minimal --no-incremental
```

Important environment note: the full solution and `DataLinq.BlazorWasm` builds fail inside the Codex sandbox because the WebAssembly/MSBuild task host cannot be created there. The same commands succeed outside the sandbox. Treat that as an environment limitation, not a code warning.

The outside-sandbox solution build currently succeeds with:

```text
318 Warning(s)
0 Error(s)
```

The per-project audit artifacts are:

- `artifacts/dev/warning-audit-after-restore-20260505.log`
- `artifacts/dev/warning-audit-after-restore-distinct-20260505.csv`

Because project builds repeat referenced project warnings, the useful planning number is the distinct source-location count:

| Warning | Distinct locations | Root cause |
| --- | ---: | --- |
| `CS0108` | 84 generated locations | Generated information-schema interfaces redeclare members inherited from manual shared interfaces. This is really 28 generated member conflicts repeated for `net8.0`, `net9.0`, and `net10.0`. |
| `CS8629` | 61 | Nullable value `.Value` use in providers and tests, mostly generated employee primary keys that are nullable before insert but known present in seeded/query results. |
| `CS8618` | 27 | Two-phase metadata/query object construction and CLI/Blazor properties that are not initialized in constructors. |
| `CS8604` | 12 | Nullable arguments passed into APIs that require concrete names, types, SQL definitions, or parameter prefixes. |
| `CS8766` | 7 | `ICOLUMNS` says properties are non-null while MySQL/MariaDB information-schema models correctly mark some provider columns nullable. |
| `CS8619` | 3 | Tuple nullability mismatches in parsing helpers. |
| `CS8602` | 2 | Possibly null dereferences in SQLite provider and one transaction compliance test. |
| `CS9107` | 2 | Primary constructor parameters are captured and also passed to base constructors in MySQL/MariaDB metadata factories. |
| `CS0414` | 1 | Dead `Program.Verbose` field in the legacy CLI. |
| `CS8600` | 1 | SQLite scalar result cast does not match possible null result. |
| `CS8603` | 1 | SQLite scalar generic can return null through a non-nullable contract. |
| `CS8620` | 1 | Nullable tuple key passed into a dictionary keyed by non-null relation identifiers. |
| `WASM0001` | 2 warning events | `SQLitePCLRaw.provider.e_sqlite3` exposes varargs native SQLite functions in the Blazor WebAssembly build. |

## Fix Strategy

### 1. Generator and Information-Schema Contracts

Files:

- `src/DataLinq.SharedCore/Factories/Generator/GeneratorFileFactory.cs`
- `src/DataLinq.MySql/Shared/IInformationSchema.cs`
- `src/DataLinq.MySql/MySql/information_schema/COLUMNS.cs`
- `src/DataLinq.MySql/MariaDB/information_schema/COLUMNS.cs`

Fix the `CS0108` and `CS8766` warnings together. The current shape is internally contradictory: `IMYSQLCOLUMNS` and `IMARIADBCOLUMNS` manually inherit `ICOLUMNS`, and the source generator also emits the same properties into those partial interfaces. C# is right to complain.

Recommended fix:

1. Keep the shared `ICOLUMNS` abstraction, because the metadata parser uses it.
2. Update `ICOLUMNS` nullability to match provider reality. If a column can be null in one supported information schema, the shared contract must say `string?` or `ulong?`.
3. Add explicit parser guards where DataLinq requires a non-null schema field, such as table name, column name, index name, or constraint name.
4. Update generator interface emission so generated properties that intentionally refine inherited interface members are emitted with `new` only when they actually hide a base-interface property.
5. Add a generator test with a model instance interface that inherits a manual interface, so this does not regress.

Avoid simply removing `ICOLUMNS` inheritance unless the parser is redesigned at the same time. Removing it would make the warning go away by weakening the shared provider contract.

### 2. Metadata Object Graph Initialization

Files:

- `src/DataLinq.SharedCore/Metadata/ColumnDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/ModelDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/PropertyDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/RelationDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/TableDefinition.cs`
- `src/DataLinq/Mutation/History.cs`
- `src/DataLinq/Exceptions/InvalidMutationObjectException.cs`

Most `CS8618` warnings here are from intentional two-phase construction. These should be fixed honestly with either `= null!` for required back-references set immediately by `TableModel`, or with nullable properties where the property can genuinely be absent.

Recommended choices:

- `TableDefinition.TableModel`, `ModelDefinition.TableModel`, `ColumnDefinition.ValueProperty`, `ValueProperty.Column`, `RelationProperty.RelationPart`, `RelationDefinition.ForeignKey`, and `RelationDefinition.CandidateKey`: use non-nullable properties with `= null!` only if every valid instance is wired before public use.
- `History.Changes`: initialize to `[]`.
- `InvalidMutationObjectException`: remove the custom uninitialized `message` field or initialize it consistently through all constructors. Prefer delegating to `base.Message` plus a normalized prefix.

This is a good place to be disciplined: do not make these nullable just to quiet the compiler if null would mean a broken metadata graph.

### 3. Query State and Nullable Parameters

Files:

- `src/DataLinq/Query/SqlQuery.cs`
- `src/DataLinq/Query/Join.cs`
- `src/DataLinq/Query/QueryUtils.cs`
- `src/DataLinq/Query/WhereGroup.cs`
- `src/DataLinq/Query/Where.cs`
- `src/DataLinq/Query/IQueryPart.cs`

The query layer has lazy state but declares some fields as always initialized. It also accepts nullable aliases and prefixes but passes them into non-nullable APIs.

Recommended fix:

- Make lazy fields explicitly nullable where they are genuinely absent until first use, or initialize them eagerly when an empty collection is the real invariant.
- Change `SetList` to `Dictionary<string, object?>`, because SQL parameters can be null.
- Make `QueryUtils.ParseTableNameAndAlias` and `ParseColumnNameAndAlias` return `(string name, string? alias)`.
- Either make `WhereGroup.AddCommandString` and related `IQueryPart` signatures accept `string? prefix`, or normalize callers with `paramPrefix ?? string.Empty`.

This should remove `CS8604`, `CS8618`, and `CS8619` in the query layer without changing generated SQL.

### 4. Provider Nullability and Runtime Guards

Files:

- `src/DataLinq.SQLite/SQLiteDbAccess.cs`
- `src/DataLinq.SQLite/SQLiteDataLinqDataReader.cs`
- `src/DataLinq.SQLite/SQLiteDatabaseTransaction.cs`
- `src/DataLinq.SQLite/SQLiteProvider.cs`
- `src/DataLinq.MySql/Shared/SqlDataLinqDataReader.cs`
- `src/DataLinq.MySql/Shared/SqlFromMetadataFactory.cs`
- `src/DataLinq.MySql/MySql/MetadataFromMySqlFactory.cs`
- `src/DataLinq.MySql/MariaDB/MetadataFromMariaDBFactory.cs`

These warnings are more mixed. Some are nullability annotations. Some are real unchecked assumptions.

Recommended fix:

- In data readers, resolve `column.ValueProperty.CsType.Type` into a non-null local with a clear exception if runtime `Type` metadata is unavailable.
- Check `EnumProperty.HasValue` before using `.Value`.
- In SQLite scalar APIs, decide the real contract. If `ExecuteScalar<T>` must return a value, use `!` at the cast boundary and let a null scalar fail clearly. If null is valid, change the base contract to `T?`.
- In `SQLiteDatabaseTransaction`, make the owned `connectionString` nullable or initialize it only for constructors that can reopen connections. Attached external transactions should not pretend to have an owned connection string.
- In MySQL/MariaDB relation and index parsing, guard nullable information-schema names before using them as dictionary keys or attribute constructor arguments.
- Replace the primary-constructor metadata factories with explicit constructors and private fields to remove `CS9107`.

### 5. CLI and Blazor Sample Initialization

Files:

- `src/DataLinq.CLI/Program.cs`
- `src/DataLinq.Blazor/Code/DL.cs`
- `src/DataLinq.Blazor/Components/Pages/Home.razor`
- `src/DataLinq.Tools/ModelGenerator.cs`

These are low-risk cleanup warnings.

Recommended fix:

- Remove the unused static `Program.Verbose` field or actually use it.
- Mark optional command-line properties nullable, or initialize them to `string.Empty` where empty string is the existing convention.
- Initialize `ConfigFile` with `null!` and keep access behind `ReadConfig`, or make it nullable and guard `ConfigBasePath`.
- In `DL.Initialize`, handle a missing `"employees"` connection string with a clear exception instead of passing null into the MySQL database constructor.
- In `Home.razor`, initialize component fields with `null!` because Blazor sets them during `OnInitializedAsync`, or make rendering conditional until initialization completes.
- In `ModelGenerator`, guard `Path.GetDirectoryName(filepath)` before `Directory.CreateDirectory`.

### 6. Compliance Test Nullable Primary Keys

Files:

- `src/DataLinq.Tests.Compliance/Translation/EmployeesContainsTranslationTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesEmptyListQueryTests.cs`
- `src/DataLinq.Tests.Compliance/Translation/EmployeesLocalAnyPredicateTests.cs`
- `src/DataLinq.Tests.Compliance/Transactions/EmployeesTransactionLifecycleTests.cs`

Most test warnings are `emp_no.Value`. The model type is nullable for a reason: new mutable employees can exist before an auto-increment key is assigned. The tests, however, are usually operating on persisted seeded rows where the key is known present.

Recommended fix:

- For local values outside expression-tree translation, use a helper such as `RequireEmployeeNumber(employee)` or `employee.emp_no ?? throw new InvalidOperationException(...)`.
- Inside LINQ expressions that must be translated, use `x.emp_no!.Value` only when the test fixture guarantees persisted rows. This is a legitimate assertion of test preconditions, not a production nullability model change.
- Do not globally make `Employee.emp_no` non-nullable just to satisfy tests. That would lie about the mutable insert lifecycle.

### 7. Blazor WebAssembly SQLite Warning

File:

- `src/DataLinq.BlazorWasm/DataLinq.BlazorWasm.csproj`

`WASM0001` is not a C# nullability issue. The WebAssembly SDK is warning that `SQLitePCLRaw.provider.e_sqlite3` contains varargs native SQLite functions such as `sqlite3_config` and `sqlite3_db_config`. The warning says calls to those functions are not supported and will fail at runtime.

Recommended decision path:

1. If the BlazorWasm smoke project does not need the SQLite provider, remove or isolate the dependency path that pulls `SQLitePCLRaw.provider.e_sqlite3` into the WebAssembly build.
2. If WebAssembly SQLite support is a real product goal, investigate a browser-wasm-safe SQLitePCLRaw provider/bundle before suppressing the warning.
3. Suppress `WASM0001` only if the smoke test deliberately proves that the affected varargs functions are never called. If suppressed, keep the suppression local to `DataLinq.BlazorWasm.csproj` with a comment explaining the runtime boundary.

Blanket suppression would be dishonest here. This warning is specifically about runtime failure, not compiler pedantry.

## Execution Order

1. Fix generator/interface contracts first. That removes the largest noisy bucket and prevents generated `obj` warnings from obscuring the source warnings.
2. Fix shared metadata/query nullability next. These warnings repeat across nearly every provider and test project, so one good slice removes a lot of duplicate output.
3. Fix provider-specific nullability and guards.
4. Clean CLI, Blazor sample, and tool warnings.
5. Clean compliance test nullable primary-key usage.
6. Decide the WebAssembly SQLite approach.
7. Once the build is clean, add warning enforcement. Start with project-level `TreatWarningsAsErrors` or CI enforcement after the cleanup, not before.

## Verification Plan

After each slice:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.sln -c Debug -v minimal --no-incremental
```

For BlazorWasm, verify outside the Codex sandbox until the WebAssembly task-host limitation is solved:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.BlazorWasm\DataLinq.BlazorWasm.csproj -c Debug -v minimal --no-incremental
```

Final validation should include:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- build src\DataLinq.sln --profile ci --output raw -- --no-incremental
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite generators --alias quick --output failures
```

If the local environment blocks NuGet restore or WebAssembly task hosts, rerun only those verification commands outside the sandbox and record that explicitly in the PR notes.
