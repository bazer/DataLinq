> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: Config JSON Schema and Autocomplete

**Status:** Draft implementation plan.
**Goal:** Provide first-class editor autocomplete and validation for `datalinq.json` and `datalinq.user.json` through a published JSON Schema, schema-aware `config init` output, a `config schema` command, and SchemaStore integration.

## Executive Position

JSON Schema is the right answer. It is boring, standard, and widely supported by the editors DataLinq users are likely to use. A good schema gives users:

- autocomplete
- enum suggestions
- hover descriptions
- validation for misspelled fields
- deprecation warnings where editors support them
- examples for tricky values such as secret references

This should be a V1 feature for config UX, not a later polish item. Configuration is one of the first things users touch. If the config feels guessy, the product feels immature even when the runtime is solid.

Recommended top of a generated config:

```json
{
  "$schema": "https://datalinq.org/schemas/datalinq.schema.json",
  "Databases": []
}
```

The public schema URL should use the real project domain:

```text
https://datalinq.org/schemas/datalinq.schema.json
```

## V1 Scope

V1 should include all of the useful pieces, not a partial local-only version:

- checked-in schema file
- schema URL in `init`-generated configs
- CLI command to print or write the embedded schema
- documentation for manual schema use
- website publication at `datalinq.org`
- SchemaStore submission
- deprecation annotations for old config fields

This is a small enough feature that doing only half of it would be mostly self-inflicted friction.

## Current Code Audit

### Config Model

`src/DataLinq/Config/ConfigFile.cs` currently defines the raw config shape:

```csharp
public record ConfigFile
{
    public List<ConfigFileDatabase> Databases { get; set; } = new();
}
```

Database fields currently include:

- `Name`
- `CsType`
- `Namespace`
- `SourceDirectories`
- `DestinationDirectory`
- `Include`
- `UseRecord`
- `UseFileScopedNamespaces`
- `UseNullableReferenceTypes`
- `CapitalizeNames`
- `RemoveInterfacePrefix`
- `SeparateTablesAndViews`
- `Connections`
- `FileEncoding`

Connection fields currently include:

- `Type`
- `DatabaseName`
- `DataSourceName`
- `ConnectionString`

Current dev plans add or rename relevant config concepts:

- `ModelDirectory`
- `ModelLayout`
- `ModelLayout.PropertyOrder`
- `ModelLayout.KeyPlacement`
- `ModelLayout.RelationPlacement`
- secret references in connection strings

The schema should reflect planned user-facing config, not merely the older internal class names.

### `$schema` Handling

`System.Text.Json` ignores unknown JSON properties by default in this config path. That means existing config reading can tolerate a top-level `$schema` property even before the config model exposes it.

For clean `init` output and future config writing, add an explicit property or a dedicated writer model:

```csharp
[JsonPropertyName("$schema")]
public string? Schema { get; set; }
```

The config runtime does not need to use this value. It is for editors.

### Comments in Config

DataLinq strips comments before JSON deserialization. That is useful for users.

Editor schema validation is separate. Some editors treat `*.json` strictly and may warn on comments even if DataLinq accepts them. The docs should be honest:

- DataLinq accepts comments in config files.
- JSON Schema autocomplete still works in common editors.
- If an editor complains about comments, configure that editor for JSON-with-comments or remove comments.

Do not make the schema file itself JSON-with-comments. Schema files should be valid JSON.

## Schema File

Add:

```text
docs/schemas/datalinq.schema.json
```

Recommended top-level schema metadata:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://datalinq.org/schemas/datalinq.schema.json",
  "title": "DataLinq configuration",
  "description": "Configuration for the DataLinq CLI and model-generation workflow.",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "$schema": {
      "type": "string",
      "description": "JSON Schema URL used by editors for autocomplete and validation."
    },
    "Databases": {
      "type": "array",
      "items": { "$ref": "#/$defs/database" }
    }
  },
  "required": [ "Databases" ]
}
```

Use draft 2020-12 unless there is a practical editor compatibility reason to choose an older draft. Most schema-aware editors can consume this just fine.

## Schema Strictness

Use:

```json
"additionalProperties": false
```

at each object level.

This is not pedantry. Catching `ModelDirecotry` instead of silently accepting it is the point of autocomplete and validation.

The compatibility cost is real: adding new config fields requires updating the schema. That is healthy. The schema should evolve with the config model.

## Main and User Configs

Use one schema for both:

```text
datalinq.json
datalinq.user.json
```

Reason: the user file can override the same database fields, and the merge model is database-name based. A separate strict user schema would either be too restrictive or require a second schema that mostly duplicates the first.

Use descriptions to guide intent:

- fields normally stored in shared config
- fields normally stored in user config
- fields accepted for compatibility only

Example:

```json
"Connections": {
  "description": "Connection entries. Prefer local or secret-bearing connection strings in datalinq.user.json rather than committed datalinq.json."
}
```

## Field Coverage

The schema should cover at least these V1 fields.

### Root

- `$schema`
- `Databases`

### Database

- `Name`
- `CsType`
- `Namespace`
- `ModelDirectory`
- `DestinationDirectory`
- `SourceDirectories`
- `Include`
- `UseRecord`
- `UseFileScopedNamespaces`
- `UseNullableReferenceTypes`
- `CapitalizeNames`
- `RemoveInterfacePrefix`
- `SeparateTablesAndViews`
- `ModelLayout`
- `Connections`
- `FileEncoding`

### ModelLayout

- `PropertyOrder`: `Column`, `Alphabetical`
- `KeyPlacement`: `Inline`, `Top`
- `RelationPlacement`: `Bottom`, `Top`, `WithForeignKey`

### Connection

- `Type`: `SQLite`, `MySQL`, `MariaDB`
- `DatabaseName`
- `DataSourceName`
- `ConnectionString`

## Deprecation Annotations

Use schema deprecation annotations where appropriate.

### SourceDirectories

Mark as deprecated:

```json
"SourceDirectories": {
  "type": "array",
  "deprecated": true,
  "description": "Deprecated. create-models now reads existing model files from ModelDirectory instead of a separate source directory."
}
```

This should remain in the schema during the compatibility window so old config files get a useful warning instead of an unknown-property error.

### DestinationDirectory

Mark as deprecated once `ModelDirectory` exists:

```json
"DestinationDirectory": {
  "type": "string",
  "deprecated": true,
  "description": "Compatibility alias for ModelDirectory. Prefer ModelDirectory in new config files."
}
```

`DestinationDirectory` should remain accepted for compatibility, but new docs and `init` output should use `ModelDirectory`.

## Secret Reference Documentation

The schema cannot validate every provider-specific secret value deeply, but it can provide examples and descriptions.

For `ConnectionString`, include examples:

```json
"examples": [
  "Data Source=app.db;Cache=Shared;",
  "Server=localhost;Database=appdb;User ID=app;Password=${env:DATALINQ_APPDB_PASSWORD};",
  "Server=localhost;Database=appdb;User ID=app;Password=${secret:datalinq/AppDb/password};",
  "Server=localhost;Database=appdb;User ID=app;Password=${prompt:AppDb password};"
]
```

Do not require secret references. Plain connection strings remain valid, especially for SQLite and non-secret local development.

## `datalinq config init` Integration

Once `config init` exists, generated main config files should include:

```json
"$schema": "https://datalinq.org/schemas/datalinq.schema.json"
```

Rules:

- add `$schema` to new `datalinq.json`
- do not add `$schema` automatically to existing configs unless the user explicitly chooses an update
- do not put `$schema` in `datalinq.user.json` by default if it would feel redundant, but it is allowed

I lean toward adding `$schema` only to the shared main config. Editors commonly associate the schema with both files through SchemaStore once that lands. The user file can still include `$schema` manually if needed.

## CLI Schema Command

Add:

```bash
datalinq config schema
```

Default behavior:

```bash
datalinq config schema
```

prints the embedded schema JSON to stdout.

Optional output:

```bash
datalinq config schema -o datalinq.schema.json
```

writes the embedded schema to a file.

Optional future flags:

```bash
datalinq config schema --url
datalinq config schema --validate datalinq.json
```

Do not add validation in V1 unless it is trivial. Editor support and schema publication are the primary goal.

### Embedding

The CLI should embed the schema as a resource or include it as content in a reliable way.

Do not make `datalinq config schema` depend on the working directory or installed docs files. A globally installed tool should be able to print its schema anywhere.

## Website Publication

Publish the schema at:

```text
https://datalinq.org/schemas/datalinq.schema.json
```

If DocFX owns the site, configure the docs build so `docs/schemas/datalinq.schema.json` is copied to:

```text
_site/schemas/datalinq.schema.json
```

Requirements:

- served with a JSON content type where possible
- stable URL
- no authentication
- no redirect to a file viewer page
- no HTML wrapper

If versioning is needed later, add:

```text
https://datalinq.org/schemas/datalinq-1.schema.json
```

but V1 can keep a single latest schema URL.

## SchemaStore Submission

Submit the published schema to SchemaStore after the public URL works.

Recommended file matches:

```json
[
  "datalinq.json",
  "datalinq.user.json"
]
```

The SchemaStore entry should point at:

```text
https://datalinq.org/schemas/datalinq.schema.json
```

This allows editors that consume SchemaStore to associate the schema automatically by filename, even when `$schema` is missing.

## Handwritten vs Generated Schema

Hand-write the schema.

Do not blindly generate it from C# config types. Generated schemas are usually technically acceptable and human-hostile. The value here is high-quality descriptions, examples, defaults, and deprecation annotations.

Add tests to keep it honest instead of auto-generating it:

- schema parses as JSON
- schema validates documented minimal examples
- schema rejects misspelled properties
- schema allows `$schema`
- schema marks `SourceDirectories` as deprecated
- schema marks `DestinationDirectory` as deprecated once `ModelDirectory` exists
- schema contains all public config fields
- provider enum values stay in sync with supported built-in providers
- model layout enum values stay in sync with implementation

## Implementation Plan

### 1. Add Schema File

Create:

```text
docs/schemas/datalinq.schema.json
```

Use draft 2020-12 and `$id`:

```text
https://datalinq.org/schemas/datalinq.schema.json
```

### 2. Add `$schema` Support to Config Writing

Add either:

```csharp
[JsonPropertyName("$schema")]
public string? Schema { get; set; }
```

to `ConfigFile`, or use a dedicated writer DTO for `init`.

Since config reading already tolerates unknown properties, this is mostly about clean generation and testing.

### 3. Update `init`

When creating a new main config, add:

```json
"$schema": "https://datalinq.org/schemas/datalinq.schema.json"
```

Do not rewrite existing configs just to add it in V1.

### 4. Add `datalinq config schema`

Add a nested config command:

```bash
datalinq config schema
```

Support stdout and optional `-o` output.

Use the embedded schema content.

### 5. Publish Through Docs Site

Configure the docs/site build to copy the schema into:

```text
schemas/datalinq.schema.json
```

under the generated site.

Verify the generated `_site` output.

### 6. Submit to SchemaStore

After the public URL is live, submit the SchemaStore entry.

This is part of V1 completion, not a later "nice to have."

### 7. Add Tests

Add unit tests for schema validity and example validation.

If no JSON Schema validator dependency exists, add a test-only package rather than implementing validation manually. Keep it out of runtime packages.

Tests should cover:

- minimal MariaDB/MySQL example
- minimal SQLite example
- main config plus user config example
- `ModelDirectory`
- `ModelLayout`
- secret-reference examples
- deprecated `SourceDirectories`
- deprecated `DestinationDirectory`
- typo rejection via `additionalProperties: false`

### 8. Update Docs

After implementation, update:

- `docs/Configuration files.md`
- `docs/getting-started/Configuration and Model Generation.md`
- `docs/CLI Documentation.md`
- `datalinq config init` docs once that command exists

Document:

- `$schema`
- editor autocomplete
- `datalinq config schema`
- public schema URL
- SchemaStore support
- how comments interact with editor validation

## Non-Goals

- Do not build a custom editor extension in V1.
- Do not generate the schema mechanically from C# types.
- Do not require `$schema` for config parsing.
- Do not remove compatibility fields from the schema during their compatibility window.
- Do not publish a schema URL under a temporary domain.
- Do not add schema validation to every CLI command in V1 unless it falls out naturally from config parsing.

## Acceptance Criteria

- The repo contains `docs/schemas/datalinq.schema.json`.
- The schema uses `$id` `https://datalinq.org/schemas/datalinq.schema.json`.
- The schema provides autocomplete/validation coverage for current and planned public config fields.
- The schema marks `SourceDirectories` as deprecated.
- The schema marks `DestinationDirectory` as deprecated once `ModelDirectory` exists.
- `datalinq config init` writes `$schema` into new main configs.
- `datalinq config schema` prints the embedded schema and can write it to a file.
- The docs site publishes the schema at `https://datalinq.org/schemas/datalinq.schema.json`.
- A SchemaStore submission is part of the V1 work.
- Tests validate representative config examples against the schema and prove misspelled fields are rejected.
