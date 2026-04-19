> [!WARNING]
> This document is a historical audit plus structure record from the TUnit migration period. It is retained as context, not as a description of the current repo state.
# Phase 0: Test Suite Audit and Target Structure

**Status:** Audit complete; retained as baseline  
**Scope:** Inventory the current test suite, classify what exists today, and define the recommended structure for the new TUnit-based suite.

## 1. Executive Summary

The current suite has **432 tests** spread across three projects:

* `src/DataLinq.Tests`: 379 tests
* `src/DataLinq.MySql.Tests`: 44 tests
* `src/DataLinq.Generators.Tests`: 9 tests

That project split looks cleaner than it really is.

The main problems are:

* `DataLinq.Tests` mixes pure unit tests, SQLite-specific integration tests, and cross-provider behavior tests.
* `DataLinq.MySql.Tests` mixes live-database integration tests with at least one pure parser/unit-style file.
* Database setup logic is duplicated across fixtures.
* The current suite depends heavily on xUnit-specific fixture and serialization patterns.
* There is at least one effectively dead or misplaced test file.

The right structure for the new suite is not "one new TUnit project." The right structure is:

* `src/DataLinq.Testing`
* `src/DataLinq.Tests.Unit`
* `src/DataLinq.Tests.Compliance`
* `src/DataLinq.Tests.MySql`
* `src/DataLinq.Generators.Tests`

That target structure is now mostly implemented. `src/DataLinq.Tests.Compliance` is no longer a proving-ground experiment; it is the real compliance lane.

## 2. Current Inventory

### 2.1. Project Summary

| Project | Total tests | Real role today | Problem |
| --- | ---: | --- | --- |
| `src/DataLinq.Tests` | 379 | Mixed unit + SQLite + cross-provider integration | Too many responsibilities in one project |
| `src/DataLinq.MySql.Tests` | 44 | Mostly MySQL/MariaDB integration and temp-schema tests | Contains one pure parser test file that does not belong with live DB tests |
| `src/DataLinq.Generators.Tests` | 9 | Generator-focused tests | This one is comparatively clean |

### 2.2. `DataLinq.Tests` Breakdown

`DataLinq.Tests` is the main source of disorder.

Within `DataLinq.Tests`, the suite currently breaks down like this:

* Pure or mostly pure in-process tests: 116 tests
* Database-dependent tests: 263 tests
* LINQ query coverage alone: 122 tests

That means the project is majority integration behavior, even though it also holds a large block of pure unit tests.

### Pure or Mostly Pure In-Process Tests

These tests do not require MySQL or MariaDB and should move out of the current mixed project:

* `src/DataLinq.Tests/Core/GeneratorFileFactoryTests.cs`
* `src/DataLinq.Tests/Core/KeyFactoryAndEqualityTests.cs`
* `src/DataLinq.Tests/Core/MetadataFactoryTests.cs`
* `src/DataLinq.Tests/Core/MetadataFromFileFactoryTests.cs`
* `src/DataLinq.Tests/Core/MetadataFromModelsFactoryTests.cs`
* `src/DataLinq.Tests/Core/MetadataFromTypeFactoryTests.cs`
* `src/DataLinq.Tests/Core/MetadataTransformerTests.cs`
* `src/DataLinq.Tests/Core/MetadataTypeConverterTests.cs`
* `src/DataLinq.Tests/Core/ModelFileFactoryTests.cs`
* `src/DataLinq.Tests/Core/SyntaxParserTests.cs`
* `src/DataLinq.Tests/WeakEventManagerTests/WeakEventManagerConcurrencyTests.cs`
* `src/DataLinq.Tests/WeakEventManagerTests/WeakEventManagerTests.cs`

Notes:

* `src/DataLinq.Tests/Core/OptimizationTests.cs` is mixed. One test is pure (`Evaluator_ShouldEvaluateLocalVariable_Correctly`), but most of the file is cross-provider database behavior. That file should be split.
* `src/DataLinq.Tests/SQLiteInMemoryTests.cs` is self-contained and does not require Podman, but it is still integration coverage, not unit coverage. It should not move into the pure unit bucket.

### Cross-Provider Compliance and Behavior Tests

These tests exercise behavior across the provider matrix and currently rely on `BaseTests` or `DatabaseFixture`:

* `src/DataLinq.Tests/CacheTests.cs`
* `src/DataLinq.Tests/CharPredicateTests.cs`
* `src/DataLinq.Tests/CoreTests.cs`
* `src/DataLinq.Tests/InstanceEqualityTests.cs`
* `src/DataLinq.Tests/MutationTests.cs`
* `src/DataLinq.Tests/RelationTests.cs`
* `src/DataLinq.Tests/SqlQueryTests.cs`
* `src/DataLinq.Tests/SqlTests.cs`
* `src/DataLinq.Tests/ThreadingTests.cs`
* `src/DataLinq.Tests/TransactionTests.cs`
* `src/DataLinq.Tests/Linq/ContainsTranslationTests.cs`
* `src/DataLinq.Tests/LinqQueryTests/BooleanLogicTests.cs`
* `src/DataLinq.Tests/LinqQueryTests/DateTimeMemberTests.cs`
* `src/DataLinq.Tests/LinqQueryTests/EmptyListTests.cs`
* `src/DataLinq.Tests/LinqQueryTests/NullableBooleanTests.cs`
* `src/DataLinq.Tests/LinqQueryTests/QueryTests.cs`
* `src/DataLinq.Tests/LinqQueryTests/StringMemberTests.cs`
* Most of `src/DataLinq.Tests/Core/OptimizationTests.cs`

These are the tests that want an explicit provider matrix in the new suite.

### SQLite-Specific Integration Tests

These are integration tests, but they are SQLite-specific rather than full provider-compliance tests:

* `src/DataLinq.Tests/MetadataFromSQLiteFactoryTests.cs`
* `src/DataLinq.Tests/SQLiteInMemoryTests.cs`

These should stay outside the pure unit bucket. They can live in the compliance project unless the SQLite-specific slice grows large enough to justify a separate project later.

### Suspicious or Misplaced Test Files

* `src/DataLinq.Tests/SourceGeneratorTests.cs`

This file currently builds a `DatabaseFixture`, clears caches, and has its only theory commented out. It is effectively dead weight and does not belong in the new structure.

### 2.3. `DataLinq.MySql.Tests` Breakdown

This project is cleaner than `DataLinq.Tests`, but still not clean enough.

### Pure Parser / In-Process Provider Tests

This file does not need a live database:

* `src/DataLinq.MySql.Tests/MetadataFromSqlFactoryDefaultParsingTests.cs`

It tests parser behavior and default-value interpretation in-process. It should not live in the same project as temp-schema container-backed integration tests.

### Live MySQL / MariaDB Integration Tests

These require real MySQL or MariaDB access and should remain separate from pure unit tests:

* `src/DataLinq.MySql.Tests/MetadataFromMySqlFactoryTests.cs`
* `src/DataLinq.MySql.Tests/MySqlMetadataFilteringTests.cs`
* `src/DataLinq.MySql.Tests/MySqlTypeMappingTests.cs`
* `src/DataLinq.MySql.Tests/PkIsFkTests.cs`
* `src/DataLinq.MySql.Tests/RecursiveRelationTests.cs`
* `src/DataLinq.MySql.Tests/MariaDB/MariaDBTypeMappingTests.cs`

These files also depend on a cluster of dedicated fixtures:

* `src/DataLinq.MySql.Tests/DatabaseFixture.cs`
* `src/DataLinq.MySql.Tests/MySqlPkIsFkFixture.cs`
* `src/DataLinq.MySql.Tests/MySqlRecursiveRelationFixture.cs`
* `src/DataLinq.MySql.Tests/MySqlMetadataFilteringTests.cs` fixture
* `src/DataLinq.MySql.Tests/MySqlTypeMappingTests.cs` fixture
* `src/DataLinq.MySql.Tests/MariaDB/MariaDBTypeMappingFixture.cs`

That fixture sprawl is exactly why the shared `DataLinq.Testing` infrastructure project is necessary.

### 2.4. `DataLinq.Generators.Tests` Breakdown

This is the cleanest project in the suite:

* `src/DataLinq.Generators.Tests/GeneratorTestBase.cs`
* `src/DataLinq.Generators.Tests/ModelGenerationLogicTests.cs`
* `src/DataLinq.Generators.Tests/SourceGeneratorTests.cs`

It is mostly self-contained and has a sensible shared base class. It should be migrated to TUnit, but it does not need to block the earlier infrastructure work.

## 3. Infrastructure and Fixture Inventory

Current shared infrastructure and fixture files:

* `src/DataLinq.Tests/BaseTests.cs`
* `src/DataLinq.Tests/DatabaseFixture.cs`
* `src/DataLinq.Tests/Helpers.cs`
* `src/DataLinq.Tests/TestAssembly.cs`
* `src/DataLinq.MySql.Tests/DatabaseFixture.cs`
* `src/DataLinq.MySql.Tests/MySqlPkIsFkFixture.cs`
* `src/DataLinq.MySql.Tests/MySqlRecursiveRelationFixture.cs`
* `src/DataLinq.MySql.Tests/MySqlTypeMappingTests.cs` fixture class
* `src/DataLinq.MySql.Tests/MySqlMetadataFilteringTests.cs` fixture class
* `src/DataLinq.MySql.Tests/MariaDB/MariaDBTypeMappingFixture.cs`

Problems:

* Fixture logic is duplicated between `DataLinq.Tests` and `DataLinq.MySql.Tests`.
* Some fixtures own environment creation, schema creation, and cleanup all at once.
* Some fixtures create temporary databases with live side effects.
* `src/DataLinq.Tests/TestAssembly.cs` disables parallelization for the entire assembly.
* Absolute Windows log paths are embedded in fixture code.

## 4. Structural Problems We Should Fix On Purpose

### 4.1. Database Dependency Is Hidden Behind `BaseTests`

`BaseTests` makes it easy to miss that many test files are not unit tests at all. Anything inheriting from it is effectively integration coverage.

That is a smell. In the new suite, the test category should be visible from the project and the harness type, not hidden in a shared base class.

### 4.2. The Provider Matrix Is Implicit

Current `MemberData` patterns pass `object[]` or database instances around without making the provider matrix explicit.

That was acceptable while the suite was small. It is not acceptable now.

The new suite should explicitly model:

* SQLite file
* SQLite in-memory
* MySQL
* MariaDB

Not every test has to run against every provider, but that choice should be explicit.

### 4.3. Unit and Integration Are Mixed Inside the Same Namespace Tree

`src/DataLinq.Tests/Core` contains mostly pure unit tests, but `OptimizationTests.cs` is mixed. The root of `DataLinq.Tests` contains both cache notification unit tests and heavy transaction integration tests.

That is exactly the sort of disorder that makes framework migrations more painful than they need to be.

### 4.4. There Are Namespace and Placement Mistakes

Examples:

* `src/DataLinq.MySql.Tests/RecursiveRelationTests.cs` declares `namespace DataLinq.Tests.Core`
* `src/DataLinq.MySql.Tests/PkIsFkTests.cs` declares `namespace DataLinq.Tests.Core`
* `src/DataLinq.Tests/SourceGeneratorTests.cs` is in the wrong project and currently does not provide meaningful coverage

These are not catastrophic bugs, but they are evidence that the current suite has drifted.

## 5. Recommended Target Structure

### 5.1. `src/DataLinq.Testing`

Purpose:

* Shared test infrastructure library
* Provider descriptors
* Test environment abstraction
* Seeding API
* Database reset helpers
* Temp schema/database helpers
* Shared test data builders

Files that conceptually belong here:

* `src/DataLinq.Tests/BaseTests.cs` replacement
* `src/DataLinq.Tests/DatabaseFixture.cs` replacement
* `src/DataLinq.Tests/Helpers.cs`
* `src/DataLinq.MySql.Tests/DatabaseFixture.cs` replacement
* All provider-specific temp fixture logic, reworked into reusable helpers instead of xUnit fixtures

This project is the backbone of the migration.

### 5.2. `src/DataLinq.Tests.Unit`

Purpose:

* Pure in-process tests
* No Podman requirement
* No live server database requirement
* Fast default test lane

Recommended contents:

* `src/DataLinq.Tests/Core/GeneratorFileFactoryTests.cs`
* `src/DataLinq.Tests/Core/KeyFactoryAndEqualityTests.cs`
* `src/DataLinq.Tests/Core/MetadataFactoryTests.cs`
* `src/DataLinq.Tests/Core/MetadataFromFileFactoryTests.cs`
* `src/DataLinq.Tests/Core/MetadataFromModelsFactoryTests.cs`
* `src/DataLinq.Tests/Core/MetadataFromTypeFactoryTests.cs`
* `src/DataLinq.Tests/Core/MetadataTransformerTests.cs`
* `src/DataLinq.Tests/Core/MetadataTypeConverterTests.cs`
* `src/DataLinq.Tests/Core/ModelFileFactoryTests.cs`
* `src/DataLinq.Tests/Core/SyntaxParserTests.cs`
* `src/DataLinq.Tests/WeakEventManagerTests/WeakEventManagerConcurrencyTests.cs`
* `src/DataLinq.Tests/WeakEventManagerTests/WeakEventManagerTests.cs`
* `src/DataLinq.MySql.Tests/MetadataFromSqlFactoryDefaultParsingTests.cs`
* The pure portion of `src/DataLinq.Tests/Core/OptimizationTests.cs`

Opinion:

This should become the main quick-feedback lane developers run all the time.

### 5.3. `src/DataLinq.Tests.Compliance`

Purpose:

* Cross-provider behavior tests
* SQLite-specific integration tests
* Shared TUnit provider matrix
* Main validation of behavioral parity across providers

Recommended contents:

* `src/DataLinq.Tests/CacheTests.cs`
* `src/DataLinq.Tests/CharPredicateTests.cs`
* `src/DataLinq.Tests/CoreTests.cs`
* `src/DataLinq.Tests/InstanceEqualityTests.cs`
* `src/DataLinq.Tests/MetadataFromSQLiteFactoryTests.cs`
* `src/DataLinq.Tests/MutationTests.cs`
* `src/DataLinq.Tests/RelationTests.cs`
* `src/DataLinq.Tests/SQLiteInMemoryTests.cs`
* `src/DataLinq.Tests/SqlQueryTests.cs`
* `src/DataLinq.Tests/SqlTests.cs`
* `src/DataLinq.Tests/ThreadingTests.cs`
* `src/DataLinq.Tests/TransactionTests.cs`
* `src/DataLinq.Tests/Linq/ContainsTranslationTests.cs`
* `src/DataLinq.Tests/LinqQueryTests/BooleanLogicTests.cs`
* `src/DataLinq.Tests/LinqQueryTests/DateTimeMemberTests.cs`
* `src/DataLinq.Tests/LinqQueryTests/EmptyListTests.cs`
* `src/DataLinq.Tests/LinqQueryTests/NullableBooleanTests.cs`
* `src/DataLinq.Tests/LinqQueryTests/QueryTests.cs`
* `src/DataLinq.Tests/LinqQueryTests/StringMemberTests.cs`
* The database-dependent portion of `src/DataLinq.Tests/Core/OptimizationTests.cs`

Opinion:

This should be the main TUnit migration target after the shared test infrastructure exists. It carries the most value and the most risk.

### 5.4. `src/DataLinq.Tests.MySql`

Purpose:

* MySQL- and MariaDB-specific integration tests
* Temp schema tests
* Type mapping tests
* Metadata parsing against live server databases

Recommended contents:

* `src/DataLinq.MySql.Tests/MetadataFromMySqlFactoryTests.cs`
* `src/DataLinq.MySql.Tests/MySqlMetadataFilteringTests.cs`
* `src/DataLinq.MySql.Tests/MySqlTypeMappingTests.cs`
* `src/DataLinq.MySql.Tests/PkIsFkTests.cs`
* `src/DataLinq.MySql.Tests/RecursiveRelationTests.cs`
* `src/DataLinq.MySql.Tests/MariaDB/MariaDBTypeMappingTests.cs`

Opinion:

These tests should not be merged into the compliance project because they are not generic behavior tests. They are provider-specific integration and schema edge-case tests.

### 5.5. `src/DataLinq.Generators.Tests`

Purpose:

* Generator-only test project
* Migrate from xUnit to TUnit without changing its identity

Recommended contents:

* Keep the existing generator tests here
* Remove or ignore `src/DataLinq.Tests/SourceGeneratorTests.cs`

## 6. Migration Priority by Slice

This is the recommended migration order.

### Slice 1: Shared Infrastructure

Build first:

* `src/DataLinq.Testing`
* Podman environment scripts
* Shared provider descriptors
* Shared seeding and reset interfaces

Without this, the TUnit migration will just reproduce the current fixture mess in a new framework.

### Slice 2: First TUnit Proving Ground

Create:

* `src/DataLinq.Tests.Compliance`

Populate it with one vertical slice:

* one cross-provider CRUD or transaction test
* one SQLite-specific test
* one MySQL/MariaDB live integration test

This project was the proving ground. It is now the permanent compliance lane for cross-provider behavior coverage.

### Slice 3: `DataLinq.Tests.Unit`

This is the easiest high-confidence migration bucket.

Why:

* Minimal infrastructure risk
* High test volume
* Fast feedback
* Good place to establish TUnit coding style and assertion conventions

### Slice 4: `DataLinq.Tests.Compliance`

This is the main event.

It should only start after the provider matrix and Podman story are stable.

### Slice 5: `DataLinq.Tests.MySql`

This comes after compliance because the provider-specific fixtures and temp-schema helpers should be built on top of the shared infrastructure rather than invented ad hoc again.

### Slice 6: `DataLinq.Generators.Tests`

This slice is now complete. The generator project stayed in place and was migrated to TUnit in its existing project.

This can be migrated in parallel later, but it does not need to block the infrastructure work.

## 7. Files That Need Special Handling

### Split Rather Than Move Wholesale

* `src/DataLinq.Tests/Core/OptimizationTests.cs`

Reason:

* It mixes a pure evaluator test with database-backed provider behavior tests.

### Retire or Replace

* `src/DataLinq.Tests/SourceGeneratorTests.cs`

Reason:

* It is misplaced and currently does not provide meaningful live coverage.

### Rework Rather Than Port Directly

* `src/DataLinq.Tests/BaseTests.cs`
* `src/DataLinq.Tests/DatabaseFixture.cs`
* `src/DataLinq.MySql.Tests/DatabaseFixture.cs`
* `src/DataLinq.MySql.Tests/MySqlPkIsFkFixture.cs`
* `src/DataLinq.MySql.Tests/MySqlRecursiveRelationFixture.cs`
* `src/DataLinq.MySql.Tests/MySqlMetadataFilteringTests.cs` fixture
* `src/DataLinq.MySql.Tests/MySqlTypeMappingTests.cs` fixture
* `src/DataLinq.MySql.Tests/MariaDB/MariaDBTypeMappingFixture.cs`

Reason:

* These embody xUnit fixture assumptions and duplicated environment ownership. They should be redesigned into reusable testing infrastructure, not transliterated into TUnit one-to-one.

## 8. Concrete Recommendations for the Next Step

The next implementation step should be:

1. Create `src/DataLinq.Testing`
2. Define the provider matrix abstraction there
3. Decide the Podman orchestration shape for MySQL and MariaDB
4. Continue building `src/DataLinq.Tests.Compliance`
5. Port a very small vertical slice

Do **not** start by mass-converting `DataLinq.Tests` file by file. The architecture is still wrong, and brute-force conversion would lock that mess in.

## 9. Bottom Line

The inventory confirms the core diagnosis:

* the suite is too mixed,
* the fixtures own too much,
* the provider matrix is implicit,
* and the new suite needs explicit structure before broad migration begins.

That is actually good news. It means the first step is clear.
