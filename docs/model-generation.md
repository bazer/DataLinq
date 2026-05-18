# Model Generation

DataLinq CLI model generation writes editable model declaration files into the configured `ModelDirectory`.

Those files are generated, but they are not the same thing as compiler-generated `.g.cs` implementation files. The declaration files are the stable model surface that DataLinq reads again on regeneration.

## Editing Generated Models

Supported edits in files under `ModelDirectory`:

- rename generated model classes
- rename scalar properties
- rename relation properties
- change C# property types, including shared enum or typed-id style types

DataLinq preserves those supported edits by matching the C# declarations back to database metadata through mapping attributes such as `[Database]`, `[Table]`, `[Column]`, `[ForeignKey]`, and `[Relation]`.

Do not edit mapping attributes just to rename C# members. For example, rename the C# property and keep `[Column("database_column")]` pointing at the real database column.

Do edit mapping attributes when the database mapping itself changed and the model file must describe that new mapping.

Put custom methods, helper properties, validation, and application behavior in separate partial classes outside the generated model declaration files.

## Regeneration

`datalinq generate models` reads existing model files from `ModelDirectory` before writing refreshed files. That is how supported class names, property names, relation names, and C# property types are preserved.

Use `--fresh` when you intentionally want to ignore the existing model files and recreate the model surface from database metadata and `datalinq.json`.

`--fresh` is destructive to supported C# surface edits because those edits are learned from the existing model files.

## Layout

Generated member order is project configuration, not a local CLI preference. Configure it in each database entry:

```json
{
  "ModelLayout": {
    "PropertyOrder": "Column",
    "KeyPlacement": "Top",
    "RelationPlacement": "Bottom"
  }
}
```

Supported values:

- `PropertyOrder`: `Column`, `Alphabetical`
- `KeyPlacement`: `Top`, `Inline`
- `RelationPlacement`: `Bottom`, `Top`, `WithForeignKey`

The default is column order, primary keys at the top, and relation properties at the bottom.

## Compiler-Generated Files

Files emitted by the source generator during compilation are implementation files. Do not edit those files. Edit the model declarations in `ModelDirectory` or ordinary partial classes instead.
