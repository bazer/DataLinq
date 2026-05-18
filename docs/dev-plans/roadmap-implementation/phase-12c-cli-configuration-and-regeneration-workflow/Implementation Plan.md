> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 12C Implementation Plan: CLI Configuration and Regeneration Workflow

## Position

Phase 12C should be one implementation phase with multiple workstreams, not several roadmap phases.

The reason is dependency shape. The command-surface change, config initialization, schema command, recursive target expansion, diagnostics, and secrets all meet at the CLI parser, config reader, command tests, and docs. Splitting them into separate roadmap phases would create artificial boundaries and force temporary command names or compatibility behavior that we already know we do not want.

The detailed source plans should stay separate. They are the right level for design decisions. This document is the execution wrapper: order, workstreams, verification, and closeout criteria.

## Global Decisions

- Use `System.CommandLine` for `DataLinq.CLI`.
- Use nested commands for new CLI shape.
- Keep exactly one old command compatibility path: `create-models` should invoke `generate models` with a deprecation warning.
- Do not keep old option aliases such as `--name`, `--type`, `--datasource`, `--skip-source`, or `validate --output`.
- Use `--format` for text/json output format.
- Use `--output` only for file paths.
- Treat config files as project policy: generation layout and model directory behavior belong in `datalinq.json`, not local flags.

## Workstream A: Parser, Command Surface, and Diagnostics

### Goals

- switch from `CommandLineParser` to `System.CommandLine`
- introduce the nested command groups
- make parser failures, command errors, and warnings look uniform
- preserve existing exit-code semantics

### Implementation Steps

1. Add a command-building layer for `DataLinq.CLI`.
2. Define global options, especially `--config`.
3. Add primary commands:
   - `generate models`
   - `generate sql`
   - `database create`
   - `validate`
   - `diff`
   - `config init`
   - `config list`
   - `config schema`
   - `config validate` when schema validation lands
   - `secrets list/set/remove` when local secrets land
4. Add only the `create-models` deprecated compatibility command.
5. Route all errors and warnings through one diagnostic writer.
6. Remove square-bracket error styles and make severity formatting consistent.
7. Support red errors and yellow warnings when color is enabled and degrade cleanly when color is disabled or unsupported.

### Verification

- command parser tests for every primary command
- parser tests proving old option names fail with useful output
- compatibility test for `create-models`
- diagnostic writer tests for error/warning format and color mode
- CLI smoke tests for help output and exit codes

### Progress

Checkpoint `30cd4b46` started Workstream A:

- replaced `CommandLineParser` with `System.CommandLine` in `DataLinq.CLI`
- added the implemented nested command surface:
  - `generate models`
  - `generate sql`
  - `database create`
  - `validate`
  - `diff`
  - `config list`
- kept only the deprecated flat `create-models` command
- renamed target options to `--database`, `--provider`, and `--data-source`
- renamed validation format output to `--format`
- rejected old command and option names through parser errors
- routed parser errors and invalid validation format errors through `ConsoleDiagnosticWriter`
- removed square-bracket diagnostic code formatting from CLI diagnostic text
- added parser and diagnostic unit coverage
- removed the now-unused `CommandLineParser` package version from central package management

The first checkpoint intentionally does not expose `config init`, `config schema`, `config validate`, or `secrets` commands yet. Those belong to later workstreams and should not appear as nonfunctional stubs.

Verification after this checkpoint:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.CLI\DataLinq.CLI.csproj -c Debug -v minimal --no-incremental`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore`
- CLI smoke checks for root help, `config list --help`, rejected `--skip-source`, and rejected `validate --output json`

Follow-up parser coverage completed the Workstream A test boundary:

- `diff` accepts the new target options and file output option
- `generate sql` requires `--output`
- parser failures return exit code `2`
- unit coverage increased to 653 passing tests

Workstream A is complete for the currently implemented command surface. Later workstreams will add `config init`, `config schema`, `config validate`, and `secrets` as real commands when their behavior lands.

## Workstream B: Model Directory and Generation Layout

### Goals

- replace the separate source/destination model-tree workflow with one `ModelDirectory`
- preserve supported model declaration edits by reading existing files from `ModelDirectory`
- add `--fresh`
- add `ModelLayout`

### Implementation Steps

1. Add `ModelDirectory` to config and keep `DestinationDirectory` as a compatibility alias during the transition.
2. Continue parsing `SourceDirectories`, but mark it deprecated in diagnostics, docs, and schema.
3. Stop using `SourceDirectories` for `generate models`, `validate`, and `diff`.
4. Make `generate models` read existing model files from `ModelDirectory` unless `--fresh` is supplied.
5. Keep `--overwrite-types` with its current meaning.
6. Add nested `ModelLayout` config:
   - `PropertyOrder`: `Column` or `Alphabetical`
   - `KeyPlacement`: `Top` or `Inline`
   - `RelationPlacement`: `Bottom`, `Top`, or `WithForeignKey`
7. Default to:
   - `PropertyOrder: "Column"`
   - `KeyPlacement: "Top"`
   - `RelationPlacement: "Bottom"`
8. Reject unknown layout values with clear config diagnostics.
9. Update generated-file header wording to explain the supported edit surface and link to docs.

### Verification

- config merge tests for `ModelDirectory`, `DestinationDirectory`, and nested `ModelLayout`
- tests proving `SourceDirectories` is ignored by generation/validation/diff
- generation tests for `--fresh`
- generation tests for preserving supported class/property/type edits
- layout tests for property/key/relation ordering
- tests for unknown layout values

### Implementation Notes

Checkpoint 1 added the config and active path migration:

- `ConfigFileDatabase` accepts `ModelDirectory` and nested `ModelLayout`.
- `DataLinqDatabaseConfig.ModelDirectory` is now the effective model path.
- `DestinationDirectory` remains accepted as a compatibility alias and resolves to the same effective value.
- Conflicting `ModelDirectory` and `DestinationDirectory` values on the same database fail clearly.
- `generate models`, `validate`, `diff`, `generate sql`, `database create`, and model reading now use `ModelDirectory`.
- `SourceDirectories` is still parsed for compatibility, but active tools warn that it is deprecated and ignore it.
- Focused tests cover the alias behavior, default/merged layout config, unknown layout values, and validation ignoring a missing source directory.

Verification after checkpoint 1:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.CLI\DataLinq.CLI.csproj -c Debug -v minimal --no-incremental`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/DataLinqConfigTests/*"`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/SchemaValidatorTests/*"`

Checkpoint 2 wired `ModelLayout` into CLI model rendering:

- `ModelFileFactoryOptions.ModelLayout` carries the effective config into generated C# model files.
- `PropertyOrder: "Column"` keeps configured column order, while `"Alphabetical"` sorts scalar properties by C# property name.
- `KeyPlacement: "Top"` moves primary-key scalar properties ahead of other scalar properties; `"Inline"` leaves them in the selected property order.
- `RelationPlacement: "Bottom"` keeps existing bottom placement, `"Top"` emits relations before scalar properties, and `"WithForeignKey"` emits many-to-one relations after the configured foreign-key scalar property group while leaving remaining relations at the bottom.
- Focused model-factory tests cover default key placement, alphabetical inline ordering, top relation placement, and foreign-key-adjacent relation placement.

Verification after checkpoint 2:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.CLI\DataLinq.CLI.csproj -c Debug -v minimal --no-incremental`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/ModelFileFactoryTests/*"`

Checkpoint 3 updated generated-file guidance:

- CLI model declaration files now say that supported model class names, property names, relation names, and C# property types may be edited.
- The declaration header links to `https://datalinq.org/docs/model-generation.html#editing-generated-models`.
- Source-generator implementation files now use a stricter header that says not to edit compiler-generated implementation files.
- Public docs now include a model-generation page backing the header link, and the CLI docs match the nested command surface introduced in Workstream A.

Verification after checkpoint 3:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.CLI\DataLinq.CLI.csproj -c Debug -v minimal --no-incremental`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/GeneratorFileFactoryTests/*"`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/ModelFileFactoryTests/*"`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Generators.Tests\DataLinq.Generators.Tests.csproj -c Debug --no-restore --treenode-filter "/*/*/ModelGenerationLogicTests/SourceGenerator_UsesSourceNullable*"`

Checkpoint 4 added generation-level coverage for the new model-directory workflow:

- first-time `generate models` succeeds when `ModelDirectory` does not exist yet
- `--fresh` behavior ignores invalid existing model files
- normal regeneration fails when existing model files are invalid
- supported edits to model class names, scalar property names, and C# property types are preserved from existing files in `ModelDirectory`

Verification after checkpoint 4:

- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/ModelGeneratorModelDirectoryTests/*"`

Workstream B is complete for the configured model-directory and layout surface. Final verification:

- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore` passed with 667 tests.
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Generators.Tests\DataLinq.Generators.Tests.csproj -c Debug --no-restore` passed with 39 tests.

## Workstream C: Batch and Recursive Commands

### Goals

- run the highest-value commands across a config or folder tree
- continue through independent failures
- prevent partial writes for batch generation

### Implementation Steps

1. Add shared config discovery for recursive mode.
2. Add target expansion for:
   - `generate models --all`
   - `generate models --recursive`
   - `validate --all`
   - `validate --recursive`
   - `config list --recursive`
3. Define target identity with config path, database name, provider, and data source.
4. Make `generate models` render every target into write plans before writing any files.
5. If any generation target fails, write nothing and return failure.
6. Make `validate` and `config list` continue through readable independent targets and print summaries.
7. Keep batch output on the console in V1. Do not add artifact output directories yet.

### Verification

- recursive discovery tests that skip `.git`, `bin`, `obj`, `node_modules`, `artifacts`, and `_site`
- target expansion tests for provider filtering
- generation no-write-on-any-failure tests
- validation aggregate exit-code tests
- `config list --recursive` read-failure continuation tests

### Progress

Checkpoint 1 added the recursive discovery foundation:

- `generate models`, deprecated `create-models`, and `validate` now expose `--all` and `--recursive` parser options.
- `config list` now exposes `--recursive` without accepting `--all`.
- recursive discovery searches from the selected config path or containing directory and skips `.git`, `bin`, `obj`, `node_modules`, `artifacts`, and `_site`.
- `config list` now lists only config contents and no longer reads generated model files as a side effect.
- `config list --recursive` reads every discovered config independently, reports config read failures, continues through the remaining configs, and returns failure if any config could not be read.

Verification after checkpoint 1:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.CLI\DataLinq.CLI.csproj -c Debug -v minimal --no-incremental`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/DataLinqCliCommandSurfaceTests/*"`

Checkpoint 2 added shared target expansion and recursive validation:

- batch target expansion now resolves concrete targets as config path, database name, provider, and data source.
- `--provider` and `--database` act as filters in batch modes.
- malformed configs and unreadable configs are converted into CLI failures instead of escaping as parser exceptions.
- recursive expansion continues through config read failures and returns both target results and independent failures.
- `validate --all` and `validate --recursive` validate every expanded target, continue through target-level failures, and return:
  - `2` when any expansion or validation target fails
  - `1` when all targets ran but schema drift was found
  - `0` when all targets ran and no drift was found
- `validate --format json` in batch mode emits one aggregate payload instead of concatenating per-target JSON documents.

Verification after checkpoint 2:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.CLI\DataLinq.CLI.csproj -c Debug -v minimal --no-incremental`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/DataLinqCliTargetResolverTests/*"`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/DataLinqCliBatchCommandTests/*"`

Checkpoint 3 added batch model generation:

- `ModelGenerator` can now render a `GeneratedModelWritePlan` without writing files.
- single-target `generate models` still uses the same public `CreateModels` behavior by rendering and immediately writing one plan.
- `generate models --all` and `generate models --recursive` render every expanded target before writing any files.
- expansion failures, render failures, and duplicate generated target paths stop the batch with `files written: no`.
- a recursive regression test covers the important failure mode: one target renders successfully, a later target fails while reading existing model files, and the successful target still writes no generated files.

Verification after checkpoint 3:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.CLI\DataLinq.CLI.csproj -c Debug -v minimal --no-incremental`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/DataLinqCliBatchCommandTests/*"`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/ModelGeneratorModelDirectoryTests/*"`

Workstream C is complete. Final verification:

- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore` passed with 674 tests.
- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.CLI\DataLinq.CLI.csproj -c Debug -v minimal --no-incremental` passed.

## Workstream D: Config Schema and Init

### Goals

- make config authoring discoverable in editors
- make new-project setup and new-developer setup guided and safe

### Implementation Steps

1. Add `docs/schemas/datalinq.schema.json`.
2. Embed the schema in the CLI.
3. Add `datalinq config schema` with stdout and optional `--output`.
4. Publish the schema at `https://datalinq.org/schemas/datalinq.schema.json`.
5. Add `$schema` to new shared configs written by `config init`.
6. Add `config init` modes:
   - new project: create `datalinq.json` and `datalinq.user.json`
   - existing shared config with missing user config: create local `datalinq.user.json`
   - existing both files: inspect and optionally test; do not rewrite blindly
7. Offer narrow `.gitignore` updates for `datalinq.user.json`.
8. Keep existing commented `datalinq.json` files untouched in V1.

### Verification

- schema validation tests for representative configs
- CLI tests for `config schema`
- init state-matrix tests
- `.gitignore` update tests
- docs-site build or targeted static verification for schema publication

### Progress

Checkpoint 1 added config schema support:

- added `docs/schemas/datalinq.schema.json` with `$id` `https://datalinq.org/schemas/datalinq.schema.json`
- covered current public config fields, `ModelLayout`, built-in provider names, and connection-string examples
- marked `SourceDirectories` and `DestinationDirectory` as deprecated in the schema
- added explicit `$schema` support to the raw config DTO
- embedded the schema in `DataLinq.CLI`
- added `datalinq config schema` with stdout output and optional `--output` / `-o`
- added schema smoke validation tests proving a representative config validates and a misspelled field is rejected

Verification after checkpoint 1:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.CLI\DataLinq.CLI.csproj -c Debug -v minimal --no-incremental`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/DataLinqConfigSchemaTests/*|/*/*/DataLinqCliCommandSurfaceTests/*"`

Checkpoint 2 added config init:

- added `datalinq config init`
- added path/state detection for:
  - missing `datalinq.json` and missing `datalinq.user.json`
  - existing shared config with missing user config
  - existing shared and user config files
  - orphaned user config without a shared config
- new-project init plans create both `datalinq.json` and `datalinq.user.json`
- generated shared configs include `$schema` and use `ModelDirectory`
- generated user configs contain full connection entries and no shared structure beyond database names
- existing main configs are not rewritten when creating a missing user config
- `.gitignore` updates use narrow config-relative entries and avoid duplicates
- fixed config comment stripping so URLs such as `https://datalinq.org/schemas/datalinq.schema.json` are preserved inside JSON strings

Verification after checkpoint 2:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.CLI\DataLinq.CLI.csproj -c Debug -v minimal --no-incremental`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/DataLinqConfigInitTests/*|/*/*/DataLinqCliCommandSurfaceTests/*"`

Checkpoint 3 published and documented the schema/init surface:

- DocFX now copies `docs/schemas/datalinq.schema.json` to `_site/schemas/datalinq.schema.json`
- CLI docs describe `config init`, `config schema`, and the batch/recursive options that landed in Workstream C
- configuration docs describe `$schema`, editor autocomplete, the public schema URL, and `config schema`
- getting-started docs now start with `datalinq config init` while still showing hand-written examples

Verification after checkpoint 3:

- `docfx build docfx.json` passed with 0 warnings and 0 errors.
- `_site/schemas/datalinq.schema.json` was generated.
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/DataLinqConfigSchemaTests/*|/*/*/DataLinqConfigInitTests/*|/*/*/DataLinqCliCommandSurfaceTests/*"`

Workstream D is complete. Final verification:

- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore` passed with 686 tests.
- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.CLI\DataLinq.CLI.csproj -c Debug -v minimal --no-incremental` passed.
- `docfx build docfx.json` passed with 0 warnings and 0 errors.

## Workstream E: Secret References

### Goals

- keep secrets out of committed config files
- support local developer secrets without normalizing plaintext
- support CI through environment variables

### Implementation Steps

1. Add secret reference parsing for:
   - `${env:NAME}`
   - `${secret:name}`
   - `${prompt:label}`
2. Resolve secret references only at CLI command boundaries where prompting and redaction are available.
3. Add a redaction registry so resolved secrets and password-like values do not leak through CLI-controlled output.
4. Add local DataLinq secret storage behind a platform abstraction.
5. Add `secrets list`, `secrets set`, and `secrets remove`.
6. Teach `config init` to prefer secret references for passwords.
7. Fail clearly when a secret backend is unavailable; do not fall back to plaintext local files.

### Verification

- parser tests for secret references
- resolver tests for env/local/prompt providers
- prompt cache tests
- redaction tests for validation/generation/provider-error paths controlled by the CLI
- local secret command tests with fake backends
- non-interactive prompt failure tests

### Progress

Checkpoint 1 added the core secret-reference implementation:

- `SecretReferenceResolver` supports `${env:NAME}`, `${secret:name}`, and `${prompt:label}`.
- Connection-string placeholders are resolved through `DbConnectionStringBuilder`, so password values containing semicolons are quoted safely instead of being concatenated into raw connection-string text.
- Whole-connection-string secret references are supported for cases such as `${secret:datalinq/AppDb/connection-string}`.
- Prompt references cache values for the current CLI invocation and fail clearly when input is non-interactive.
- Resolved values, raw secret references, and password-like connection-string values are registered with the CLI redactor.
- CLI config loading resolves secret references at command boundaries before constructing `DataLinqConfig`.
- `datalinq secrets list`, `datalinq secrets set <name>`, and `datalinq secrets remove <name>` are wired into the nested command surface.
- The local secret store uses Windows Credential Manager on Windows and fails clearly on platforms where a secure backend has not landed yet.
- `config init` now defaults MySQL/MariaDB password-bearing connection strings to `${prompt:<data-source> password}` rather than normalizing plaintext passwords.

Verification after checkpoint 1:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.CLI\DataLinq.CLI.csproj -c Debug -v minimal --no-incremental`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/DataLinqSecretReferenceTests/*"`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/DataLinqSecretCommandTests/*"`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/DataLinqCliCommandSurfaceTests/*"`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-restore --treenode-filter "/*/*/CliDiagnosticWriterTests/*"`

## Documentation Workstream

Update docs only after behavior lands:

- `docs/CLI Documentation.md`
- `docs/Configuration files.md`
- getting-started configuration/model-generation docs
- generated-file edit-surface docs
- troubleshooting docs for config, secrets, and schema validation
- schema publication docs

Docs should use the new command surface only, except when explicitly explaining the temporary `create-models` deprecation path.

## Sequencing Notes

Parser and diagnostics should go first. Every later feature needs command groups, target options, and diagnostic output; delaying that work would force temporary implementations.

`ModelDirectory` should land before batch generation. Batch generation's no-write guarantee is much easier to reason about once generation has one read/write directory and one render-before-write path.

`config init` should land after the config model and schema are stable enough that the wizard writes the shape we actually want users to keep.

Secrets can land after `config init`, but the best final UX comes from teaching `config init` to write secret references once both features exist.

## Risks and Mitigations

| Risk | Severity | Mitigation |
| --- | --- | --- |
| CLI parser migration changes existing behavior accidentally | High | Add parser tests before broad command rewiring and preserve exit-code tests |
| Old command docs and new command docs diverge | Medium | Update user docs only after implementation, and keep roadmap docs marked as roadmap material |
| Batch generation partially writes files after a late failure | High | Render every target first, then write only if all render plans succeeded |
| Secret values leak through logs or exceptions | High | Register redactions at resolution time and centralize CLI output formatting |
| `config init` rewrites commented configs destructively | High | V1 should not rewrite existing shared configs |
| Schema claims planned fields as shipped behavior | Medium | Schema can include planned compatibility fields only when parser behavior supports them |

## Verification Rollup

Minimum closeout verification:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.CLI\DataLinq.CLI.csproj -c Debug
.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug --no-build
```

Add focused Testing CLI smoke runs for provider-backed generation/validation when touching provider metadata or live database paths.

Run a docs build when user-facing docs or schema publication paths change.

## Exit Criteria

Phase 12C can close when:

- parser, command, and diagnostic behavior is covered by tests
- generation config and layout behavior is covered by tests
- batch/recursive command behavior is covered by tests
- config schema and init behavior is covered by tests
- secret reference resolution and redaction are covered by tests
- user-facing docs describe the shipped CLI accurately
- roadmap implementation pointers mark Phase 12C complete and Phase 13 as the next implementation priority
