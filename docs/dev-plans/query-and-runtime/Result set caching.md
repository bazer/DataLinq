> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
### **DataLinq: Specification for Dependency-Tracked Result-Set Caching**

**Status:** Future design note. Roadmap execution is Phase 16. This depends on Phase 11 invalidation primitives, the shared freshness vocabulary, and stronger join/projection semantics; it is not current shipped behavior.

#### **1. Vision and Principle: Beyond TTL Caching**

**The Problem:** Modern applications often have complex, read-heavy views (dashboards, reports, aggregated lists) that are computationally expensive to generate on every request. The standard solution is to use an application-level cache (like `IMemoryCache`) with a fixed Time-To-Live (TTL), for example, "cache this dashboard for 5 minutes."

This is a crude compromise. A 5-minute TTL means data can be stale for up to 4 minutes and 59 seconds, while still forcing expensive re-computations every 5 minutes even if nothing has changed.

**The DataLinq Principle:** Caching should be based on data *validity*, not arbitrary timers. A cached result is valid until the moment one of its underlying data sources is modified.

**The Phase 16 vision:** Provide a first-class mechanism for caching *computed results* such as view models or DTOs, then validate those results against the rows and tables they actually read. The goal is not a magic transparent LINQ cache. The safer shape is an explicit application-cache coordination feature: user code chooses the computation boundary, while DataLinq records enough dependency information to decide whether a stamped result is still trustworthy.

---

#### **2. The Developer Workflow: An Explicit and Intuitive API**

The developer's interaction with this feature should be simple, explicit, and centered around a clear, logical workflow.

1.  **Check for a Valid Cached Result:** Before doing any work, the application will ask DataLinq if a previously computed and "stamped" result is still valid.

2.  **Use the Cache or Re-compute:**
    *   If DataLinq confirms the result is still valid, the application can instantly return the cached object.
    *   If the result is missing or invalid, the application proceeds to re-compute it.

3.  **Enter a "Tracking Scope":** To begin re-computation, the developer will create a new, temporary "Cached Scope" from the database object. This scope acts as a special read-only context.

4.  **Perform All Reads:** All database reads required to build the final result will be performed through this tracking scope. The scope transparently observes and records every unique row that is read.

5.  **Compute and Stamp the Result:** After all data is fetched and the final result object (e.g., a `DashboardViewModel`) is computed, the developer will "stamp" this result with the dependency information collected by the scope.

6.  **Store the Stamped Result:** The final, stamped result object is then placed into a standard application-level cache, ready for the next request.

This workflow empowers the developer to precisely define the boundaries of a cached computation, while DataLinq handles the complex dependency tracking and validation automatically.

---

#### **3. Core Concepts**

To achieve this vision, DataLinq will introduce several new high-level concepts.

*   **The Cached Scope:** A disposable, read-only data source that creates a transactional boundary for reads. Its primary purpose is to monitor and record the identities of all data rows accessed within its lifetime.

*   **The Dependency Fingerprint:** When a Cached Scope is completed, the collection of all unique rows it observed is converted into a "Dependency Fingerprint." This is a lightweight, serializable piece of metadata containing at least:
    1.  the table and provider-key identity of the row
    2.  a freshness marker for the dependency at the moment it was read

    The marker format is deliberately not fixed here. It could be an invalidation generation, a provider version token, a provider-specific hash, or a combination. Row hashing is an optional precision tool, not a hard prerequisite for the first result-cache slice.

*   **The Stamped Result:** A "stamped" object is any user-created object (like a DTO or ViewModel) that has a Dependency Fingerprint attached to it. This attachment is invisible to the user's code but allows DataLinq to later identify the object and its dependencies.

*   **The Validation Check:** This is the process of taking a stamped result, extracting its fingerprint, and checking the stored dependency markers against current invalidation or row-state markers. The check should avoid reloading full rows. If any dependency can no longer be proven fresh, the result is invalid.

---

#### **4. Dependencies on Current Cache and Query Foundations**

This feature is a natural evolution of the cache, invalidation, and query-shape work that now sits behind Phase 16.

1.  **Phase 11 invalidation and freshness vocabulary:** Result validation needs explicit database/table/provider-key invalidation APIs, invalidation envelopes, and shared freshness terms. Those primitives now exist as the foundation this design should consume.

2.  **Phase 12 cache-footprint accounting and cleanup behavior:** Result-set caching must report and bound its own overhead honestly. It should not reuse old row-payload byte terminology or pretend stamped result metadata is free.

3.  **Phase 13 and Phase 14 join semantics:** Dependency tracking has to understand joined rows, relation-aware joins, left-join nullability, and projection boundaries. Otherwise it will either over-invalidate everything or under-track the rows that actually make a computed result stale.

4.  **Projection and view semantics:** A result cache is only credible when the computation boundary is explicit. Views, projections, aggregates, and DTO construction need enough diagnostics to explain what was tracked and what was intentionally unsupported.

Phase 16 should start from these shipped foundations and design the smallest useful validation model. Provider hash/version retrieval can improve precision later, but requiring it up front would turn result-set caching into a provider-versioning project before the explicit tracking API has proven itself.
