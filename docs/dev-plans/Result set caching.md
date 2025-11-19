### **DataLinq: Specification for Dependency-Tracked Result-Set Caching**

#### **1. Vision and Principle: Beyond TTL Caching**

**The Problem:** Modern applications often have complex, read-heavy views (dashboards, reports, aggregated lists) that are computationally expensive to generate on every request. The standard solution is to use an application-level cache (like `IMemoryCache`) with a fixed Time-To-Live (TTL), for example, "cache this dashboard for 5 minutes."

This is a crude compromise. A 5-minute TTL means data can be stale for up to 4 minutes and 59 seconds, while still forcing expensive re-computations every 5 minutes even if nothing has changed.

**The DataLinq Principle:** Caching should be based on data *validity*, not arbitrary timers. A cached result is valid until the moment one of its underlying data sources is modified.

**The Vision for v0.8:** To provide a first-class mechanism for caching *computed results* (like view models or DTOs) and have DataLinq automatically and instantly invalidate them when—and only when—the source data they depend on actually changes. This will enable developers to cache complex results indefinitely, with the confidence that they will be automatically refreshed the moment they become stale.

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

*   **The Dependency Fingerprint:** When a Cached Scope is completed, the collection of all unique rows it observed is converted into a "Dependency Fingerprint." This is a lightweight, serializable piece of metadata containing two key pieces of information for each dependency:
    1.  The unique **Primary Key** of the row.
    2.  A **Version Marker** (a hash) representing the exact state of that row at the moment it was read.

*   **The Stamped Result:** A "stamped" object is any user-created object (like a DTO or ViewModel) that has a Dependency Fingerprint attached to it. This attachment is invisible to the user's code but allows DataLinq to later identify the object and its dependencies.

*   **The Validation Check:** This is the process of taking a stamped result, extracting its fingerprint, and performing a highly optimized check against the database. The check does not re-load the data; it simply asks the database for the *current* version markers of all the rows listed in the fingerprint and compares them to the markers stored in the fingerprint. If any marker has changed, the result is invalid.

---

#### **4. Dependencies on the Memory management Specification**

This feature is a natural evolution that builds directly on top of the foundational work planned for v0.7.

1.  **Row Versioning via Hashing:** The concept of a "Version Marker" in the Dependency Fingerprint is synonymous with the row hash feature from the v0.7 plan. The ability for the database provider to calculate a hash for any given row is a **hard prerequisite**.

2.  **Optimized Hash Retrieval:** The "Validation Check" must be extremely fast to be effective. It cannot involve re-reading full rows. This necessitates a new, optimized capability in the database providers to efficiently fetch *only the current hashes* for a given batch of primary keys. This aligns with the v0.7 goal of improving provider-specific performance and capabilities.

By building on these v0.7 features, the v0.8 implementation can focus purely on the logic of scope management, dependency tracking, and validation, rather than reinventing the underlying versioning mechanism.