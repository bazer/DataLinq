> [!NOTE]
> This checklist tracks legacy-to-TUnit cutover status. It is the current source of truth for which old test files have real replacements, which ones are intentionally retired, and which legacy projects still remain only for transition purposes.
# Test Suite Parity Checklist

**Status:** Active cutover checklist

## 1. Active TUnit Structure

The active suite layout is now:

- `src/DataLinq.Generators.Tests`
- `src/DataLinq.Tests.Unit`
- `src/DataLinq.Tests.Compliance`
- `src/DataLinq.Tests.MySql`
- `src/DataLinq.Testing`
- `src/DataLinq.Testing.CLI`

That is the real structure. Anything still living only in the legacy xUnit projects is transitional by definition.

## 2. Legacy `DataLinq.Tests` Mapping

| Legacy file or slice | Replacement status | New home |
| --- | --- | --- |
| `CacheNotificationManagerTests.cs` | Migrated | `src/DataLinq.Tests.Unit/CacheNotificationManagerTests.cs` |
| `Core/GeneratorFileFactoryTests.cs` | Migrated | `src/DataLinq.Tests.Unit/Core/GeneratorFileFactoryTests.cs` |
| `Core/KeyFactoryAndEqualityTests.cs` | Migrated | `src/DataLinq.Tests.Unit/Core/KeyFactoryAndEqualityTests.cs` |
| `Core/MetadataFactoryTests.cs` | Migrated | `src/DataLinq.Tests.Unit/Core/MetadataFactoryTests.cs` |
| `Core/MetadataFromFileFactoryTests.cs` | Migrated | `src/DataLinq.Tests.Unit/Core/MetadataFromFileFactoryTests.cs` |
| `Core/MetadataFromModelsFactoryTests.cs` | Migrated | `src/DataLinq.Tests.Unit/Core/MetadataFromModelsFactoryTests.cs` |
| `Core/MetadataFromTypeFactoryTests.cs` | Migrated | `src/DataLinq.Tests.Unit/Core/MetadataFromTypeFactoryTests.cs` |
| `Core/MetadataTransformerTests.cs` | Migrated | `src/DataLinq.Tests.Unit/Core/MetadataTransformerTests.cs` |
| `Core/MetadataTypeConverterTests.cs` | Migrated | `src/DataLinq.Tests.Unit/Core/MetadataTypeConverterTests.cs` |
| `Core/ModelFileFactoryTests.cs` | Migrated | `src/DataLinq.Tests.Unit/Core/ModelFileFactoryTests.cs` |
| `Core/SyntaxParserTests.cs` | Migrated | `src/DataLinq.Tests.Unit/Core/SyntaxParserTests.cs` |
| `Core/Evaluator` coverage from `OptimizationTests.cs` | Migrated | `src/DataLinq.Tests.Unit/Core/EvaluatorTests.cs` |
| `WeakEventManagerTests/*` | Migrated | `src/DataLinq.Tests.Unit/WeakEventManagerTests/*` |
| `MetadataFromSQLiteFactoryTests.cs` | Migrated | `src/DataLinq.Tests.Unit/SQLite/MetadataFromSQLiteFactoryTests.cs` |
| `CacheTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/State/EmployeesCacheTests.cs` |
| `CharPredicateTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Translation/CharPredicateTranslationTests.cs` |
| `CoreTests.cs` | Largely migrated | spread across `src/DataLinq.Tests.Compliance/Query`, `State`, and `Transactions` |
| `InstanceEqualityTests.cs` | Migrated | `src/DataLinq.Tests.Unit/Core/MutableInstanceEqualityTests.cs` and `src/DataLinq.Tests.Compliance/State/EmployeesInstanceEqualityTests.cs` |
| `MutationTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/State/EmployeesMutationTests.cs` |
| `RelationTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Relations/EmployeesRelationAndThreadingTests.cs` |
| `SQLiteInMemoryTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Query/SQLiteInMemoryBehaviorTests.cs` |
| `SqlQueryTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Sql/EmployeesSqlQueryTests.cs` |
| `SqlTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Sql/EmployeesSqlBuilderTests.cs` |
| `ThreadingTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Relations/EmployeesRelationAndThreadingTests.cs` |
| `TransactionTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Transactions/EmployeesTransactionTests.cs` and `EmployeesTransactionLifecycleTests.cs` |
| `Linq/ContainsTranslationTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Translation/EmployeesContainsTranslationTests.cs` |
| `LinqQueryTests/BooleanLogicTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Translation/EmployeesBooleanLogicTests.cs` |
| `LinqQueryTests/DateTimeMemberTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Translation/EmployeesDateTimeMemberTests.cs` |
| `LinqQueryTests/EmptyListTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Translation/EmployeesEmptyListQueryTests.cs` |
| `LinqQueryTests/NullableBooleanTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Translation/EmployeesNullableBooleanTests.cs` |
| `LinqQueryTests/QueryTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Query/EmployeesQueryBehaviorTests.cs` |
| `LinqQueryTests/StringMemberTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Translation/EmployeesStringMemberTests.cs` |
| Database optimization coverage from `Core/OptimizationTests.cs` | Migrated | `src/DataLinq.Tests.Compliance/Query/EmployeesOptimizationTests.cs` |
| `SourceGeneratorTests.cs` | Intentionally retired | superseded by `src/DataLinq.Generators.Tests`; old file was effectively dead |

## 3. Legacy `DataLinq.MySql.Tests` Mapping

| Legacy file or slice | Replacement status | New home |
| --- | --- | --- |
| `MetadataFromSqlFactoryDefaultParsingTests.cs` | Migrated | `src/DataLinq.Tests.MySql/MetadataFromSqlFactoryDefaultParsingTests.cs` |
| `MetadataFromMySqlFactoryTests.cs` | Migrated | `src/DataLinq.Tests.MySql/MetadataFromServerFactoryTests.cs` |
| `MySqlMetadataFilteringTests.cs` | Migrated | `src/DataLinq.Tests.MySql/MetadataFromServerFactoryTests.cs` |
| `MySqlTypeMappingTests.cs` | Migrated | `src/DataLinq.Tests.MySql/ServerTypeMappingTests.cs` |
| `MariaDB/MariaDBTypeMappingTests.cs` | Migrated | `src/DataLinq.Tests.MySql/MariaDbGuidTypeMappingTests.cs` and `ServerTypeMappingTests.cs` |
| `PkIsFkTests.cs` | Migrated | `src/DataLinq.Tests.MySql/MetadataFromServerFactoryTests.cs` |
| `RecursiveRelationTests.cs` | Migrated | `src/DataLinq.Tests.MySql/MetadataFromServerFactoryTests.cs` |
| metadata merge coverage | Migrated | `src/DataLinq.Tests.MySql/MetadataMergeTests.cs` |

## 4. Legacy `DataLinq.Generators.Tests` Mapping

| Legacy file or slice | Replacement status | New home |
| --- | --- | --- |
| `GeneratorTestBase.cs` | Migrated in place | `src/DataLinq.Generators.Tests/GeneratorTestBase.cs` |
| `ModelGenerationLogicTests.cs` | Migrated in place | `src/DataLinq.Generators.Tests/ModelGenerationLogicTests.cs` |
| `SourceGeneratorTests.cs` | Migrated in place | `src/DataLinq.Generators.Tests/SourceGeneratorTests.cs` |

## 5. Infrastructure Replacement

| Legacy infrastructure | Replacement status | New home |
| --- | --- | --- |
| `src/DataLinq.Tests/BaseTests.cs` | Replaced | `src/DataLinq.Testing` |
| `src/DataLinq.Tests/DatabaseFixture.cs` | Replaced | `src/DataLinq.Testing/Employees`, `src/DataLinq.Testing/Lifecycle` |
| `src/DataLinq.MySql.Tests/DatabaseFixture.cs` | Replaced | `src/DataLinq.Testing/Lifecycle` and `src/DataLinq.Tests.MySql/ServerSchemaDatabase.cs` |
| old PowerShell Podman scripts | Replaced | `src/DataLinq.Testing.CLI` |

## 6. Remaining Cutover Work

The migration is no longer blocked by missing TUnit coverage. The remaining work is administrative and operational:

- keep the legacy xUnit projects quarantined rather than treating them as the active suites
- keep CI pointed at the new suite structure
- delete legacy projects only once the team is comfortable removing the transition safety net

That is the right place to be. The difficult migration work is already done.
