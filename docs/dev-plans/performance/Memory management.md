> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
### **Specification for Advanced Caching and Memory Management**

**Status:** Older broad cache specification. Phase 10 owns generated key/cache identity, Phase 11 owns explicit cache clearing and external invalidation, and Phase 12 owns memory-pressure cleanup and measured deduplication.

#### **1. Overview**

This document outlines the major features and architectural changes for DataLinq. The primary goals for this release are to significantly reduce the memory footprint of the caching system, introduce more sophisticated and robust cache invalidation strategies, and provide a more powerful and intuitive programmatic configuration API.

This will be achieved through five main initiatives:
1.  **Programmatic Configuration API:** Introduce a fluent API for runtime configuration of the ORM.
2.  **Global Key Deduplication:** Implement a central cache for all primary and foreign keys to eliminate redundant key objects.
3.  **Database Profiling & Adaptive Heuristics:** Enable DataLinq to make intelligent default decisions about caching based on database and usage analysis.
4.  **Enhanced Cache Invalidation:** Introduce row versioning via hashing, hooks for external invalidation, and more granular internal invalidation.
5.  **Adaptive System Worker:** Make the background cache cleanup process proactive and responsive to system load and memory pressure.

---

#### **2. Programmatic Configuration API**

##### **2.1. Rationale**
Runtime behavior (caching policies, hashing algorithms) should be managed within the application's code, not through the CLI-only `datalinq.json` file. A programmatic API is needed for global configuration and dynamic overrides of attribute-based settings.

##### **2.2. Proposed Design**
A fluent configuration builder will be introduced via a new `Database` constructor overload.

```csharp
// Example of the new API
var db = new MySqlDatabase<EmployeesDb>(
    connectionString,
    config => {
        config.UseGlobalCache(true)
              .SetGlobalCacheLimit(CacheLimitType.Megabytes, 512)
              .SetGlobalCacheEvictionPolicy(CacheCleanupType.Minutes, 15)
              .UseRowVersioning(HashingAlgorithm.MD5)

              .ForTable("employees", tableConfig => {
                  tableConfig.SetCacheLimit(CacheLimitType.Rows, 10000);
                  tableConfig.Preload(PreloadType.Indices); // Preload only indices
              })

              .ForTable("departments", tableConfig => {
                  tableConfig.Preload(PreloadType.All); // Preload indices and rows
              });
    });
```

##### **2.3. Configuration Precedence**
The final effective configuration for any setting will be determined in the following order of priority (highest first):
1.  **Programmatic API:** Settings provided in the constructor override everything else.
2.  **Attributes:** Attributes on a model (e.g., `[CacheLimit]`) override database-wide settings.
3.  **Heuristics:** Default behaviors determined by the adaptive caching engine (see section 5).
4.  **DataLinq Defaults:** The library's hardcoded default values.

---

#### **3. Key Identity and Cache Keys**

The old version of this plan proposed a global `IKey` cache. Do not use that design as current guidance.

The better direction is the newer [Generated Provider-Key Cache Design](Generated%20Provider-Key%20Cache%20Design.md): generated primary-key and relation access should move cache lookups toward provider CLR values and table-specific accessors instead of preserving `IKey` as the universal hot-path abstraction.

That distinction matters. Deduplicating `IKey` objects would optimize around the abstraction causing the allocation problem. The newer design removes or bypasses that abstraction in the generated runtime path.

---

#### **4. Enhanced Cache Invalidation Strategies**

##### **4.1. Row Versioning with Hashing**
An optimistic concurrency model based on row hashing will be introduced to prevent stale data writes and validate cache freshness.

*   **Workflow:**
    1.  **On Read:** The database provider will append a hash column to `SELECT` queries (e.g., `MD5(CONCAT(col1, col2...))`). This hash is stored with the `RowData`.
    2.  **On Save/Update:** Before an `UPDATE` or `DELETE`, the provider re-queries the row's current hash from the database.
        *   If hashes match, the operation proceeds.
        *   If hashes differ, an exception is thrown, the transaction is rolled back, and the cache entry is invalidated.
*   **Configuration:** The hashing algorithm (`MD5`, `CRC32`, etc.) will be configurable via the new programmatic API. `MD5` will be the default.

##### **4.2. External Cache Invalidation (Event-Driven)**
An API will be provided to allow external systems (e.g., a message queue like Kafka) to trigger direct cache invalidation.

*   **Proposed API:**
    ```csharp
    public interface ICacheInvalidator
    {
        void Invalidate(TableDefinition table, IReadOnlyList<object?> providerPrimaryKey);
        // ... other methods
    }

    // Method on the main Database<T> object
    database.Cache.Invalidate(ICacheInvalidator invalidator);
    ```

##### **4.3. Granular Relation Invalidation**
The `OnRowChanged` notification will be made more intelligent and granular.

*   **Concept:** When a row is modified, instead of broadcasting a generic "table changed" event, the system will use the `IndexCache`'s reverse mappings (`primaryKey -> foreignKeys`) to identify precisely which related entities are affected.
*   **Benefit:** Only the specific `ImmutableRelation` and `ImmutableForeignKey` instances that are directly affected by the change will have their caches cleared. This will drastically reduce unnecessary cache misses and re-queries in highly relational applications.

---

#### **5. Database Profiling and Adaptive Caching Heuristics**

##### **5.1. Startup Analysis & Heuristics**
Upon initialization, DataLinq will perform a lightweight profiling of the database to make intelligent default decisions about caching.

*   **Actions:**
    1.  Execute `COUNT(*)` for each table to get row counts.
    2.  Estimate the in-memory size of each table.
*   **Default Heuristics:** These stats will be used to apply defaults (if not overridden):
    *   **Small Table Preloading:** Automatically preload all rows and indices for small lookup tables (e.g., total size < 1 MB).
    *   **Index Preloading:** Automatically preload indices for medium-sized tables to accelerate relation lookups.
    *   **Caching Policy:** May adjust caching behavior for exceptionally large tables.

##### **5.2. Usage-Based Cache Eviction (Frequency Caching)**
A frequency counter will be implemented to make eviction policies smarter.

*   **Concept:** Track the access frequency of each cached object. When memory limits are reached, the cache will evict the Least Frequently Used (LFU) items first. A periodic decay mechanism will ensure that recent activity is prioritized.

---

#### **6. Adaptive System Worker (`CleanCacheWorker`)**

The background worker responsible for cache maintenance will be made more responsive to the application's state.

##### **6.1. Adaptive Cleanup Interval**
The worker's fixed-interval sleep cycle will be replaced with a dynamic interval that adapts to the rate of data mutation ("churn").

*   **Implementation:**
    1.  A counter will track the number of commits since the last cleanup.
    2.  The worker will calculate its next sleep duration based on this churn rate, running more frequently during high activity and less frequently during idle periods, within a configurable `MinInterval` and `MaxInterval`.

##### **6.2. System Memory Pressure Awareness**
The worker will react to application-wide memory pressure.

*   **Concept:** Utilize `System.GC.GetGCMemoryInfo()` to detect high GC pressure.
*   **Benefit:** If high pressure is detected, the worker will trigger an immediate, and potentially more aggressive, cleanup cycle. This makes DataLinq a better citizen within the host application, helping to prevent memory-related issues.
