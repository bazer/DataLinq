> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 12C: CLI Configuration and Regeneration Workflow

**Status:** Complete as of 2026-05-18.

## Purpose

Phase 12C turns the CLI from a collection of useful commands into a coherent pre-1.0 product surface.

The user-facing rule is simple: DataLinq should be easy to initialize, safe to regenerate, predictable in config-driven output, friendly to solution-wide validation, and consistent in how it names commands, options, errors, warnings, schemas, and secrets.

This is not a continuation of Phase 12 cache-memory work or Phase 12B diagnostics hardening. The `12C` name is a practical insertion point so the existing Phase 13 and later numbering does not churn.

## Execution Boundary

In scope:

- migrate `DataLinq.CLI` to `System.CommandLine`
- adopt the nested command surface: `generate`, `database`, `config`, and `secrets`
- keep only `create-models` as a temporary deprecated command alias for `generate models`
- standardize target options as `--database`, `--provider`, and `--data-source`
- standardize CLI warnings and errors, including red/yellow coloring where supported
- replace the separate `SourceDirectories`/`DestinationDirectory` workflow with `ModelDirectory`
- read existing models from `ModelDirectory` during regeneration and add `--fresh` for database-only generation
- add `ModelLayout` config for property, key, and relation ordering
- add `generate models --all/--recursive`, `validate --all/--recursive`, and `config list --recursive`
- add `config init` for new projects and missing local user configs
- add JSON Schema publication and `config schema`
- add secret references and `secrets list/set/remove`
- update user-facing docs only after behavior lands

Out of scope:

- migration execution or `add-migration` / `update-database`
- batch `diff`, `generate sql`, or `database create`
- cloud secret managers
- a full project-template system
- a solution-file parser
- query API expansion, relation-aware joins, or runtime caching work

## Source Plans

- [CLI Command Surface Redesign](../../../tooling/CLI%20Command%20Surface%20Redesign.md)
- [CLI Diagnostics Output Style](../../../tooling/CLI%20Diagnostics%20Output%20Style.md)
- [Model Directory Regeneration Workflow](../../../metadata-and-generation/Model%20Directory%20Regeneration%20Workflow.md)
- [Create Models Layout Configuration](../../../metadata-and-generation/Create%20Models%20Layout%20Configuration.md)
- [CLI Batch and Recursive Targets](../../../tooling/CLI%20Batch%20and%20Recursive%20Targets.md)
- [CLI Init Wizard](../../../tooling/CLI%20Init%20Wizard.md)
- [CLI Secret References](../../../tooling/CLI%20Secret%20References.md)
- [Config JSON Schema and Autocomplete](../../../tooling/Config%20JSON%20Schema%20and%20Autocomplete.md)

## Recommended Order

1. Migrate the CLI parser and diagnostics infrastructure first.
2. Land the new command surface and option vocabulary.
3. Convert generation config to `ModelDirectory` and `ModelLayout`.
4. Add batch/recursive target expansion on top of the cleaned command surface.
5. Add config schema/autocomplete and `config init`.
6. Add secret references and local secret commands.
7. Update user docs and getting-started examples once behavior is real.

## Exit Criteria

Phase 12C is done when:

- the public CLI help shows the new nested command surface
- only `create-models` remains as a deprecated compatibility command
- `generate models` uses `ModelDirectory`, ignores `SourceDirectories`, and supports `--fresh`
- generated model layout follows `ModelLayout` config defaults and rejects unknown layout values
- batch generation writes no files unless every selected target renders successfully
- `validate --all/--recursive` and `config list --recursive` continue through independent target/config failures and report aggregate exit codes
- `config init` can create both config files for a new project and create a missing `datalinq.user.json` for a cloned project
- `config schema` prints or writes the embedded schema, and the docs site publishes the schema under `datalinq.org`
- secret references support `${env:...}`, `${secret:...}`, and `${prompt:...}` with redaction
- user docs describe the shipped behavior without presenting roadmap material as current behavior
