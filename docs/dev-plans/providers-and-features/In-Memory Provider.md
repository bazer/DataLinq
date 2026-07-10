> [!WARNING]
> This document is superseded roadmap material. It is not normative product documentation and should not be treated as a shipped support claim.

# In-Memory Provider

**Status:** Superseded by the memory backend design folder.

The current design direction for DataLinq's in-memory backend is tracked in [Memory Backend Architecture](../backends/memory/Architecture.md). The immediate 0.9 implementation slice is tracked in [0.9 Read-Only Memory Backend Implementation Plan](../roadmap-implementation/v0.9/In-Memory%20Database%20Implementation%20Plan.md).

The older idea on this page framed the provider as a fully ACID in-memory database and suggested that the DataLinq cache could effectively become the database engine. That is not the right architecture.

The current direction is stricter:

- `DataLinqQueryPlan` should become backend-executable.
- SQL providers should execute plans through a SQL backend adapter.
- The memory provider should execute plans directly over metadata-driven row buffers.
- Generated metadata should be the schema.
- AOT and browser/WebAssembly should be baseline constraints.
- The cache should remain a materialized-object cache above the provider store, not the provider store itself.
- Durability should not be implied.

Keep this file only as a redirect for older links.
