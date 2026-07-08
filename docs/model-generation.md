# Model Generation

DataLinq CLI model generation writes editable model declaration files into the configured `ModelDirectory`.

Those files are generated, but they are not the same thing as compiler-generated `.g.cs` implementation files. The declaration files are the stable model surface that DataLinq reads again on regeneration.

## Generated File Types

There are two generated-file categories:

| File kind | Where it comes from | Should you edit it? |
| --- | --- | --- |
| Model declaration files | `datalinq generate models` writes them into `ModelDirectory`. | Yes, within the supported edit surface below. |
| Compiler-generated implementation files | The source generator emits them during build. | No. They are implementation output. |

The distinction is not cosmetic. Model declaration files are source input for later regeneration. Compiler-generated files are disposable build output.

## Editing Generated Models

Supported edits in files under `ModelDirectory`:

- rename generated model classes
- rename scalar properties
- rename relation properties
- change C# property types when the provider value can still be represented by the mapping metadata
- move generated enum declarations into shared source files and keep the model property pointing at that enum type

DataLinq preserves those supported edits by matching the C# declarations back to database metadata through mapping attributes such as `[Database]`, `[Table]`, `[Column]`, `[ForeignKey]`, and `[Relation]`.

Do not edit mapping attributes just to rename C# members. For example, rename the C# property and keep `[Column("database_column")]` pointing at the real database column.

Do edit mapping attributes when the database mapping itself changed and the model file must describe that new mapping.

Put custom methods, helper properties, validation, and application behavior in separate partial classes outside the generated model declaration files.

Unsupported edits include:

- deleting or lying in mapping attributes while the database schema still has the old shape
- adding behavior that depends on constructor side effects in generated model declaration files
- editing compiler-generated implementation files
- expecting `--fresh` to preserve renamed members or custom property types
- expecting arbitrary value-object member queries to work just because a property has a custom C# type

Custom C# property types are preservation, not a full scalar-converter system. The 0.9 roadmap aims to make provider-value conversion first-class. Until that lands, keep custom type edits boring and verify the generated model with real queries and writes before relying on them.

## Custom Behavior

Put behavior in ordinary partial classes:

```csharp
namespace MyApp.Models;

public abstract partial class Employee
{
    public string DisplayName => $"{first_name} {last_name}";
}
```

That file should live outside `ModelDirectory` if you want regeneration to leave it alone. The generated declaration stays focused on database mapping; your partial type owns application behavior.

## Regeneration

`datalinq generate models` reads existing model files from `ModelDirectory` before writing refreshed files. That is how supported class names, property names, relation names, and C# property types are preserved.

Use `--fresh` when you intentionally want to ignore the existing model files and recreate the model surface from database metadata and `datalinq.json`.

`--fresh` is destructive to supported C# surface edits because those edits are learned from the existing model files.

Use `--overwrite-types` when you want database-inferred C# property types to replace preserved source property types. That is a blunt tool; it is useful after removing a custom type strategy, not as a routine regeneration habit.

Use `--stamp-generated-header` when you want generated file provenance in the header:

```bash
datalinq generate models -n AppDb --stamp-generated-header
```

The stamp includes the CLI version and one UTC timestamp shared by the generated files from that run. Leave it off for deterministic regenerated source diffs.

Use `--all` or `--recursive` only when you actually want batch regeneration across all matching targets.

After regeneration, use [Schema Validation and Diff](Schema%20Validation%20and%20Diff.md) when you need to prove the generated model still matches the live database or produce a reviewable additive SQL suggestion script.

## Enum Preservation

MySQL and MariaDB `ENUM` columns can generate C# enum declarations. Regeneration handles the common source-preservation cases:

- if the enum is not represented in source yet, generation can emit a C# enum declaration
- if the enum declaration is already in the model file, regeneration keeps it
- if the property references an enum declared elsewhere, regeneration keeps the property type and `[Enum(...)]` metadata without duplicating the enum declaration

SQLite has no native enum metadata. Define enum semantics in C# when you need them.

## Nullable Context

Generated model files start with a nullable directive. The default is nullable reference generation on:

```csharp
#nullable enable
```

Set `"UseNullableReferenceTypes": false` on the database config to emit:

```csharp
#nullable disable
```

The directive makes generated files compile with the intended nullable interpretation regardless of the consuming project's `<Nullable>` setting.

## Write Safety

Generation builds and renders a complete write plan before replacing files. If metadata reading or model rendering fails, existing generated files are not replaced.

File writes are staged through temporary files. If a write fails after replacement starts, DataLinq attempts to roll back committed files from backups and reports whether rollback succeeded.

## Layout

Generated member order is project configuration, not a local CLI preference. Configure it in each database entry:

```json
{
  "ModelLayout": {
    "PropertyOrder": "Column",
    "PrimaryKeyPlacement": "Top",
    "ForeignKeyPlacement": "Inline",
    "RelationPlacement": "Bottom"
  }
}
```

Supported values:

- `PropertyOrder`: `Column`, `Alphabetical`
- `PrimaryKeyPlacement`: `Top`, `Inline`
- `ForeignKeyPlacement`: `Top`, `Inline`
- `RelationPlacement`: `Bottom`, `Top`, `WithForeignKey`

The default is column order, primary keys at the top, foreign keys inline with the selected scalar property order, and relation properties at the bottom.

## Compiler-Generated Files

Files emitted by the source generator during compilation are implementation files. Do not edit those files. Edit the model declarations in `ModelDirectory` or ordinary partial classes instead.
