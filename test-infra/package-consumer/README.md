# Package consumer fixture

This standalone multi-targeted application consumes exact local candidate versions of
`DataLinq`, `DataLinq.Memory`, `DataLinq.SQLite`, and `DataLinq.MySql`. It deliberately has no
project references and lives outside `src` so repository-only build and central-package settings
cannot turn package evidence into a source-tree build accidentally.

The fixture executes generated-model seed, lookup, and query operations through the public Memory
API; creates, inserts, queries, and updates a public shared-cache in-memory SQLite database; verifies
that a MariaDB-scoped `uuid` model column retains dashed SQLite Text36 storage without an explicit
SQLite UUID declaration; and compiles and loads the public MySQL database surface without requiring
a server. Its final standard-output line
is schema `v0.9.package-consumer-execution.v1` JSON, and any mismatch returns a nonzero exit code.

Supply `DataLinqCandidateVersion` during restore, build, and run. Release tooling must also constrain
the `DataLinq*` package source to the requested fresh local feed and use an isolated package cache;
an ordinary global NuGet cache is not acceptable release provenance.
