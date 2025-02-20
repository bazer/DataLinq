## Metadata Structure

DataLinq’s source generator relies on a rich metadata model to describe both the database schema and the corresponding object models. This metadata serves as the foundation for generating the strongly typed immutable and mutable classes, as well as their related interfaces and extension methods. The key components of this metadata structure are outlined below.

### 1. **DatabaseDefinition**

- **Purpose:**  
  Represents an entire database, including its name, caching policies, and the collection of tables/views (encapsulated as TableModels).
  
- **Key Points:**
  - Holds global attributes (such as caching limits and cleanup settings) that apply to the database.
  - Contains a set of `TableModels` which tie together table definitions and their associated model definitions.
  - Maintains a C# type declaration that is used to generate the main database class.

### 2. **TableModel and TableDefinition**

- **TableModel:**  
  Acts as the bridge between the database and the model. It links:
  - **ModelDefinition:** The description of the C# model.
  - **TableDefinition:** The structure of the underlying table or view.
  - A designated property name (used in generated code) that represents the table within the database class.

- **TableDefinition:**  
  Describes a single database table or view.
  - Contains the database table name (`DbName`), a collection of `ColumnDefinition` objects, and an array of primary key columns.
  - Supports indices via a collection of `ColumnIndex` objects, which are later used for relation mapping and performance optimizations.
  - Indicates whether the definition represents a table or a view, and holds any caching configuration specific to the table.

### 3. **ColumnDefinition and ColumnIndex**

- **ColumnDefinition:**  
  Represents a single column in a table.
  - Specifies the column’s database name, the associated database types (through `DatabaseColumnType`), and flags such as whether the column is a primary key, auto-incremented, nullable, or part of a foreign key.
  - Links to a `ValueProperty` that holds the C# type information and additional attributes (e.g., default values).

- **ColumnIndex:**  
  Describes an index over one or more columns.
  - Records the index name, type (such as BTREE, FULLTEXT, etc.), and its characteristic (e.g., primary key, unique).
  - Aggregates columns that participate in the index and supports relation mapping by storing associated `RelationPart` objects.

### 4. **CsTypeDeclaration**

- **Purpose:**  
  Encapsulates C# type information for models and properties.
  
- **Key Points:**
  - Stores the type’s name, namespace, and categorizes it (e.g., Class, Record, Interface, Primitive).
  - Used extensively during source generation to ensure that generated code uses the correct type names and that interface prefixes are removed as needed.
  - Supports both runtime types (via reflection) and syntax-based types (from Roslyn), ensuring consistency between the defined models and the generated output.

### 5. **ModelDefinition and PropertyDefinition**

- **ModelDefinition:**  
  Captures the definition of a model class.
  - Includes its C# type declaration, a collection of using directives, and a list of properties.
  - Differentiates between value properties and relation properties, and records any model-level attributes.
  - Serves as the blueprint for generating the immutable and mutable classes.

- **PropertyDefinition:**  
  The base abstraction for model properties.
  - **ValueProperty:** Represents a simple column mapping, including type information, nullability, size, and any default values or enumeration details.
  - **RelationProperty:** Represents relationships between models (foreign key associations), holding a reference to a `RelationPart` that links the property to the corresponding column index and relation definition.

### 6. **RelationDefinition and RelationPart**

- **RelationDefinition:**  
  Defines a relationship between two tables.
  - Typically represents a one-to-many relationship, specifying the constraint name and linking the foreign key side to the candidate key side.
  
- **RelationPart:**  
  Describes one side of a relationship (either the foreign key or candidate key).
  - Associates with a `ColumnIndex` and includes a C# name that is used in the generated model to reference the relationship.
  - Provides helper methods to navigate to the “other side” of the relation, enabling bidirectional navigation in the ORM.

---

### Summary

The metadata structure in DataLinq forms a comprehensive representation of the database schema and its corresponding C# models. It is divided into:
- **Database and Table Definitions:** Which capture the overall database and its individual tables or views.
- **Column and Index Definitions:** Which detail the structure of each table and support the mapping of relations.
- **Model and Property Definitions:** Which describe the C# representations of the data, including type details, attributes, and relationships.
- **Relation Structures:** Which define how tables are linked through foreign keys and candidate keys.

This metadata is then consumed by the source generator to produce consistent, strongly typed model classes that adhere to DataLinq’s design principles of immutability, efficient caching, and seamless querying.