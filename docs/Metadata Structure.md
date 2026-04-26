## Metadata Structure

The metadata model is the hinge between schema discovery, source models, and generated code.

If you are debugging generation or provider behavior, this is the layer you need to understand.

## 1. `DatabaseDefinition`

- Represents an entire database.
- Carries database name, C# type information, cache settings, and the collection of `TableModel` entries.
- Becomes the root object used by generation and provider logic.

## 2. `TableModel`, `TableDefinition`, and `ViewDefinition`

- **`TableModel`**
  - Bridges the generated C# surface to the underlying table or view metadata.
  - Knows both the model definition and the database definition it belongs to.
- **`TableDefinition`**
  - Describes a concrete table.
  - Stores columns, primary keys, indices, cache settings, and table type.
- **`ViewDefinition`**
  - Extends the same idea for views.
  - Can also carry the SQL definition used for view generation.

## 3. `ColumnDefinition` and `ColumnIndex`

- **`ColumnDefinition`**
  - Describes a single database column.
  - Stores db name, db types, nullability, primary-key state, auto-increment state, and foreign-key state.
  - Links to the `ValueProperty` that describes the C# side.
- **`ColumnIndex`**
  - Describes an index across one or more columns.
  - Stores name, type, characteristic, and relation parts.

## 4. `CsTypeDeclaration`

- Encapsulates C# type information for models and properties.
- Used by metadata factories and the source generator to keep names, namespaces, and type categories coherent.

## 5. `ModelDefinition`, `ValueProperty`, and `RelationProperty`

- **`ModelDefinition`**
  - Describes the C# model itself.
  - Stores type information, attributes, using directives, and properties.
- **`ValueProperty`**
  - Represents a scalar property mapped to a column.
  - Stores C# type, db type metadata, defaults, enum metadata, and nullability.
- **`RelationProperty`**
  - Represents a navigable relation between models.
  - Links back to relation metadata so generated models can expose relation accessors.

## 6. `RelationDefinition` and `RelationPart`

- **`RelationDefinition`**
  - Represents a relationship between two tables.
  - Connects foreign-key and candidate-key sides.
- **`RelationPart`**
  - Represents one side of that relationship.
  - Associates with a `ColumnIndex` and carries the C# relation name used by generated code.

### Summary

The metadata layer is where DataLinq decides what the database means in C# terms. The source generator, query system, relation handling, and provider logic all depend on this model agreeing on the same truth.
