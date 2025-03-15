# DataLinq Documentation Index

Welcome to the DataLinq documentation! This index is designed to help you quickly find the resources you need—whether you’re just starting to use DataLinq or diving into advanced customization and backend development.

---

## 1. Getting Started & Usage

### [CLI Documentation](CLI%20Documentation.md)
Provides an overview of the DataLinq CLI tool and its commands (`create-database`, `create-sql`, `create-models`, `list`) along with usage examples.

### [Configuration Files](Configuration%20files.md)
Describes the structure and options for `datalinq.json` and `datalinq.user.json`, explaining how to configure your database connections, model generation settings, and more.

### Querying (TBD)

### Mutation (TBD)

### Transactions (TBD)

### Required, Optional and Default Values (TBD)

### Supported LINQ Queries (TBD)

### Alternative Query Syntax (TBD)

### Cache Invalidation (TBD)

### Attributes (TBD)

### API Documentation with Examples (TBD)

---

## 2. Internals & Architecture

### [Project Specification](Project%20Specification.md)
Outlines DataLinq’s overarching goals, design principles, and architectural vision.

### [Technical Documentation](Technical%20documentation.md)
Offers an in-depth overview of the library’s core components, covering caching, mutation, query processing, and general design decisions.

### [Metadata Structure](Metadata%20Structure.md)
Explains how DataLinq maps databases to C# models through its metadata model—covering tables, columns, relationships, and more.

### [Source Generator](Source%20Generator.md)
Describes how DataLinq’s source generator creates immutable and mutable model classes from the metadata, minimizing boilerplate and ensuring consistency.

### [Query Translator](Query%20Translator.md)
Details how LINQ expressions are converted into backend-specific SQL, including an explanation of expression visitors and other helper classes.

### [Caching & Mutation](Caching%20&%20Mutation.md)
A dedicated guide to DataLinq’s caching architecture (including the primary-key-first approach) and mutation workflow (immutable data, transactional updates, commits, and rollbacks).

---

## 3. Extensibility & Advanced Development

### [Implementing a new backend](Implementing%20a%20new%20backend.md)
Walks through creating support for additional data sources by implementing metadata readers, SQL generation logic, and data read/write classes behind DataLinq’s provider interfaces.

#### Additional Topics

- **[Contribution Guidelines](Contributing.md)**  
  A guide for new contributors covering coding standards, how to run tests, and submission guidelines.
  
- **Testing & Benchmarking (TBD)**  
  Documentation detailing how to run and write tests for DataLinq, as well as interpret performance benchmarks.

- **FAQ / Troubleshooting (TBD)**  
  A list of frequently asked questions, common issues, and troubleshooting tips.

- **Migration & Extensibility (TBD)**  
  Guidance on migrating from earlier versions or more advanced customizations—some aspects are partially covered in “Implementing a new backend.”