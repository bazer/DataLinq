## Source Generator Overview

The DataLinq source generator automates the creation of immutable and mutable model classes, along with associated interfaces and extension methods. Its primary goal is to eliminate boilerplate code while ensuring that the generated models accurately reflect the underlying database schema and developer-defined attributes. The source generator accomplishes this by analyzing existing source code to build a comprehensive metadata representation, then using that metadata to produce additional source files that are incorporated into the compilation.

---

## Key Components and Workflow

### 1. **Model and Syntax Collection**

- **Syntax Provider:**  
  The generator starts by scanning the source code using Roslyn’s syntax provider.  
  - It identifies candidate model declarations by checking for classes that implement one of the key model interfaces (e.g., `ITableModel`, `IViewModel`, or custom variants).
  - The predicate function (`IsModelDeclaration`) quickly filters out irrelevant syntax nodes, while a transformation function extracts the corresponding `TypeDeclarationSyntax` for further analysis.

### 2. **Metadata Extraction**

- **SyntaxParser:**  
  Processes the collected syntax trees to extract model information.
  - It parses class declarations to create a `ModelDefinition` that includes C# type details, properties, attributes, and using directives.
  - It distinguishes between value properties and relation properties, building a detailed blueprint for each model.

- **Metadata Factories:**  
  Two primary factories convert syntax into metadata:
  - **MetadataFromModelsFactory:**  
    Consumes the `TypeDeclarationSyntax` nodes to produce a `DatabaseDefinition` that aggregates all model definitions, table definitions, and relational mappings.
  - **MetadataFromFileFactory:**  
    Offers an alternative approach by reading source files from specified directories, enabling external models to be integrated into the metadata.

- **MetadataFactory and Transformers:**  
  - The **MetadataFactory** converts `ModelDefinition` instances into `TableDefinition` or `ViewDefinition` objects, applying attributes such as `[Table]`, `[UseCache]`, and caching limits.
  - The **MetadataTransformer** further refines the metadata, for example, by removing interface prefixes and updating constraint names as needed.
  - **MetadataTypeConverter** assists by mapping C# type names to their database equivalents and calculating sizes and nullability.

### 3. **File Generation**

- **GeneratorFileFactory:**  
  This component is responsible for producing the output files based on the extracted metadata.
  - It defines options such as namespace, tab indentation, and whether to generate records or use file-scoped namespaces.
  - The factory constructs file headers (including using directives and namespace declarations), generates the body of the file by combining model properties, attributes, and method definitions, and then appends footers.
  - It produces files for both the main database definition and each individual table or view model.

- **ModelFileFactory:**  
  Further refines the file generation for individual models.
  - It creates files that include generated interfaces, immutable class definitions, mutable class definitions, and extension methods.
  - This component ensures that all aspects of a model (from column mapping to relation handling) are represented in the generated code.

### 4. **Integration into Compilation**

- **ModelGenerator (IIncrementalGenerator):**  
  The entry point for the source generator.
  - It registers the syntax provider to continuously monitor changes in the source code.
  - The generator combines the collected syntax nodes with the overall compilation and then passes them to the metadata factories.
  - Generated files are then added to the compilation context via `context.AddSource`, ensuring that they become part of the project without requiring manual inclusion.

- **Configuration and Options:**  
  The generator checks compilation options (such as nullable reference types) and applies settings accordingly. This ensures that generated code aligns with the project’s language version and coding standards.

---

## Summary

The DataLinq source generator operates in four key phases:

1. **Collection:**  
   It scans the codebase for model declarations using Roslyn’s syntax provider.

2. **Metadata Extraction:**  
   It transforms syntax nodes into rich metadata representations, capturing database schema, column definitions, relations, and model attributes.

3. **File Generation:**  
   Using the metadata, it generates source files that define immutable and mutable models, interfaces, and extension methods. These files include all necessary attributes, property definitions, and helper methods for CRUD operations.

4. **Compilation Integration:**  
   The generated files are seamlessly added to the compilation, ensuring that the ORM remains in sync with the underlying model definitions.

This modular approach minimizes boilerplate, enforces consistency, and allows developers to focus on business logic rather than repetitive code. The source generator’s design also facilitates easy customization and extension, making it a core strength of the DataLinq project.