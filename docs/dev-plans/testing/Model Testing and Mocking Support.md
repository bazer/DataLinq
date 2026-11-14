# Model Testing and Mocking Support

**Accepted 0.10 scope, 2026-11-14.** Application tests use the public `DataLinq.Testing` package to create an isolated SQLite fixture, seed it through the existing session, and run the application's real unit of work. The package owns setup, lifetime and DI replacement. The repository harness will move to the non-packable `DataLinq.TestInfrastructure` project. See the [W6 implementation contract and examples](../roadmap-implementation/v0.10/W6%20Testing%20Support.md).

## Choosing the test boundary

| What the test needs to establish | What to arrange | What to assert |
|---|---|---|
| A rule operating on already supplied values | Ordinary application inputs, or the existing collection double for an API taking `IImmutableRelation<T>` | The returned value or business decision |
| A handler inserts or updates related rows | Seed a fresh SQLite fixture and pass its factory into the handler | Returned values and committed rows, read after the handler finishes |
| Several writes must succeed or fail together | Arrange a real FK or uniqueness violation on the second write | The application's failure outcome and the original stored state of the first row, checked by direct SQLite read or after clearing the fixture cache |
| The hosted application uses the intended database | Replace one DataLinq registration before building the host, then resolve the normal service | Its writes reach the intended fixture, including through generated services and keyed/default aliases |
| The application contacts an external service | Supply a test implementation of the application's own interface | The interaction the application contract promises |
| Behavior depends on SQL dialect, locking or connection failures | Use the relevant provider or controlled provider harness | The outcome for that provider |

The existing [collection double](../../../src/DataLinq/Instances/ImmutableRelation.cs) already handles stand-alone collections. The new package adds zero public mock types. SQLite supplies actual immutable rows, relation reads, constraints and transaction completion. A public recorder or failure-injection API needs an independent use case before it enters a later release.

## Seeding and relations

Use the same `Seed` or `SeedAsync` callback for one row or several. Use ordinary C# factories for deterministic mutables, insert the principal, then assign its returned key to each dependent within the callback. Return the inserted models or a tuple. Success means the transaction has committed and cleaned up. A failed seed publishes no result. The [fixture contract](../roadmap-implementation/v0.10/W6%20Testing%20Support.md#fixture-contract) covers store isolation, schema creation, foreign keys and disposal.

The November 1 graph-builder decisions, including typed handles and automatic key propagation, are deferred with the builder. Relation-aware inserts, binding precedence and dependency ordering belong to the [production mutation design](../query-and-runtime/Relation-Aware%20Mutation%20API.md). New Memory integration is also deferred.

## Hosting and evidence

An existing fixture is borrowed by the test host. The replacement routes the selected model/key, default alias, read root and generated services to the fixture's real factory. Enabled W5 startup validation sees the initialized SQLite schema. The host releases the resources it owns before the test disposes the fixture. [W6 DI replacement](../roadmap-implementation/v0.10/W6%20Testing%20Support.md#di-replacement-and-w5-startup)

The release requires successful and failing stateful workflows, independent fixture lifetime, correct DI routing, and external .NET 8/9/10 package consumers. SQLite verifies application behavior on SQLite. Provider suites establish MySQL and MariaDB behavior. [Implementation sequence and gates](../roadmap-implementation/v0.10/W6%20Testing%20Support.md#implementation-sequence-and-gates)
