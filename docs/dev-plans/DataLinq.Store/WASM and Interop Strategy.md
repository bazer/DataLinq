> [!WARNING]
> This document is incubating platform-strategy material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store WASM And Interop Strategy

**Status:** Draft strategy.

## Purpose

DataLinq.Store should treat browser WebAssembly as a first-class target, but it should not require Blazor.

The browser story should be:

```text
DataLinq.Store runtime
  -> browser WebAssembly
  -> JavaScript export facade
  -> optional Blazor adapter
  -> optional framework adapters
```

Blazor is an important consumer. It should not be the runtime boundary.

## Runtime Targets

### Browser WASM Without Blazor

This is the primary constrained-platform target for a JavaScript-facing Store runtime.

The Store runtime should expose a coarse JavaScript API around:

- store creation
- subscription registration
- module snapshot reads
- module patch application
- command dispatch
- command status observation
- hydration and serialization
- diagnostics

The JavaScript boundary should not expose one interop call per node field. Interop should batch work by command, module patch, module snapshot, or subscription notification.

### Blazor WebAssembly

Blazor should use the same core store runtime as the JavaScript facade.

The Blazor package should add:

- DI registration
- component-friendly subscription helpers
- render batching
- error/loading/stale state helpers
- optional integration with browser storage

It should not fork store semantics.

### Local JavaScript Hosts

Node, Deno, Bun, and other JavaScript hosts are useful future targets, but they should not drive the first design.

The first local-host value is testability:

- protocol tests
- patch application tests
- serialization compatibility tests
- JavaScript facade smoke tests

The production local-host story should wait until the browser API is stable.

### WASI And Component Model

WASI/component-model support is a separate future track.

It may eventually be useful for plugin hosts, edge runtimes, or non-JavaScript embedding. It should not be the initial Store runtime target because the DataLinq.Store use case needs rich object state, callbacks, subscriptions, serialization, and UI integration. Browser WebAssembly with JavaScript interop is the more immediate fit.

## AOT Position

DataLinq.Store should be AOT-friendly by design, but it should support more than one publish mode.

Recommended stance:

```text
Browser light state:
  WASM no-AOT may be useful if the generated state core runs cleanly and payload size matters most.

Browser heavy state/querying:
  WASM AOT should be supported for CPU-heavy local indexing, patching, projection, and derived subscriptions.

iOS and constrained mobile:
  The generated state core should avoid dynamic code so mobile AOT modes can consume it.

Server and CLI:
  Native AOT is useful for specialized hosts, but normal JIT .NET remains a valid server runtime.
```

The project should not assume browser AOT reduces payload size. Browser AOT usually improves runtime speed at the cost of larger downloads. Payload work should focus on package boundaries, trimming, generated metadata, and avoiding unnecessary provider dependencies.

## AOT-Safe Runtime Rules

The supported browser path should avoid:

- `Reflection.Emit`
- hot-path `Expression.Compile()`
- runtime model discovery
- arbitrary `IQueryable` provider execution in the browser
- broad reflection over model assemblies
- dynamically loaded plugins
- per-row reflection
- serializer reflection where source-generated contexts are practical

The supported browser path should prefer:

- generated table descriptors
- generated module descriptors
- generated node and edge descriptors
- generated property accessors
- generated primary-key access
- generated serializer metadata
- generated query descriptors
- compact DTOs for protocol messages
- explicit unsupported-path diagnostics

Compatibility fallbacks can exist for server or desktop hosts, but they should not be silently used in the AOT/browser support boundary.

## JavaScript API Shape

The JavaScript facade should be small and coarse.

Sketch:

```ts
const store = await DataLinqStore.create({
  schema: generatedSchema,
  transport: syncTransport
});

const subscription = await store.subscribeModule("ProjectWorkspace", {
  projectId: 42
});

subscription.onSnapshot(snapshot => {
  render(snapshot.nodes, snapshot.edges);
});

await store.dispatch("RenameEmployee", {
  employeeId: 1,
  name: "Ada"
});
```

The facade should hide .NET runtime details once initialized. Consumers should not need to know about row-cache internals, generated metadata hooks, or DataLinq provider details.

## TypeScript Declarations

TypeScript declarations are a major usability feature, but they do not have to be in the first runtime slice.

Eventual generation target:

```text
module parameter types
module node types
module edge types
command parameter types
module snapshot/patch message types
store facade types
```

The generator should come from DataLinq model metadata and named module/command declarations, not from hand-written TypeScript.

## Blazor API Shape

The Blazor adapter should feel natural to .NET users:

```csharp
var subscription = await Store.SubscribeAsync(
    StoreModules.ProjectWorkspace(projectId),
    cancellationToken);

var snapshot = subscription.Current;
```

Components need reliable render behavior:

- initial loading state
- stale state
- error state
- disposal unsubscribes
- patch batching avoids repeated renders
- optimistic command state is observable

The adapter should not require components to know whether the underlying transport is SignalR, WebSocket, in-process, or test fake.

## Persistence Strategy

Do not start with SQLite/OPFS as a requirement.

Recommended order:

1. in-memory store
2. deterministic JSON/binary hydration format
3. IndexedDB persistence adapter
4. sync replay and stale-state recovery
5. SQLite/OPFS experiment

SQLite/OPFS may become valuable for offline-heavy relational applications, but it adds payload size, native WebAssembly warnings, and storage complexity. It should be an adapter, not the baseline.

## Payload Discipline

The browser payload target should be honest:

- the Store core should not pull database providers by default
- SQLite should be opt-in
- Blazor should be opt-in
- server sync should be opt-in
- generator/analyzer assets should not become runtime WebAssembly payload
- payload reports should exclude symbols when discussing deployed size

Every optional feature that pulls a major dependency should live behind a separate package or explicit reference.

## Testing Strategy

Minimum browser/WASM evidence:

- store initializes in browser without Blazor
- generated schema loads
- module snapshot applies
- module patch applies transactionally
- optimistic overlay applies and rolls back
- subscription callback fires once per committed patch
- stale/invalidate state works
- JS facade smoke passes
- Blazor adapter smoke passes
- AOT publish succeeds for supported path
- no-AOT publish is tested separately and either supported or explicitly marked unsupported

Do not infer browser support from server tests. Browser support needs browser smoke tests.

## Open Questions

- Should the first browser facade use raw JS exports, a generated npm package, or both?
- Should the first transport use SignalR because it is convenient for .NET users, or raw WebSocket because it keeps the protocol cleaner?
- Should no-AOT browser support be a first-class target if the Store core avoids SQLite and dynamic query execution?
- Should TypeScript generation happen in the DataLinq generator, a Store generator, or a separate tooling package?
- Should local JavaScript host support be validated with Node first, Deno first, or left as a later compatibility lane?
