> [!WARNING]
> This document is incubating architecture material. It is not shipped DataLinq behavior and should not be treated as a support claim.

# DataLinq.Store API And Binding Generation

**Status:** Draft specification.

## Purpose

DataLinq.Store needs a developer-friendly way to bind server modules, commands, and client-side state code into browser and Blazor applications.

The goal is:

> Define Store contracts once, then generate the server endpoint adapters, C# client proxy, WebAssembly export facade, and JavaScript/TypeScript bindings from the same contract.

This should let developers write:

- server-side C# modules and commands
- optional client-side C# orchestration code
- JavaScript/TypeScript UI code that calls generated bindings
- Blazor UI code that uses generated C# methods directly

The binding layer should make the ergonomic path the correct path. JavaScript should call the generated Store facade, not bypass the state manager with ad hoc `fetch(...)` calls.

## Design Stance

Use contract-first Store APIs, not arbitrary ASP.NET action scraping.

ASP.NET actions are too broad as the source of truth:

- route attributes
- filters
- custom model binders
- file uploads
- streaming
- OpenAPI gaps
- authorization conventions
- cancellation behavior
- transport-specific details

Those are valid ASP.NET features, but they are not a clean state-sync contract.

DataLinq.Store should define its own narrow contract surface:

- modules subscribe and hydrate state
- commands mutate state
- queries/selectors read loaded module state
- client actions orchestrate UI/state behavior

ASP.NET integration should be generated from that contract.

## Contract Shape

Conceptual sketch:

```csharp
[StoreApi]
public interface IProjectStoreApi
{
    [StateModule]
    ValueTask<ProjectWorkspaceModule> ProjectWorkspace(
        ProjectWorkspaceParams parameters,
        CancellationToken cancellationToken);

    [StoreCommand]
    ValueTask RenameTask(
        RenameTaskCommand command,
        CancellationToken cancellationToken);
}
```

The syntax is open. The contract requirements are not:

- stable module and command ids
- generated request/response DTOs
- explicit cancellation behavior
- explicit authorization hooks
- explicit serialization metadata
- compatibility/version metadata
- client-safe error contracts

## Generated Outputs

The generator should produce several layers.

### Server ASP.NET Adapter

Generated server output:

- endpoint mapping
- route names or hub method names
- request parsing
- response serialization
- cancellation wiring
- authorization hook calls
- validation hook calls
- command dispatch wrapper
- module subscription/hydration wrapper
- `System.Text.Json` source-generation context

Example generated shape:

```csharp
app.MapDataLinqStore<ProjectStoreApi>(options =>
{
    options.RequireAuthorization();
});
```

The generated adapter can target HTTP endpoints, SignalR, WebSocket, or an in-process test transport. The contract should not depend on one transport.

### C# Client Proxy

Generated C# client output:

- typed command methods
- typed module subscription methods
- typed request DTOs
- typed response DTOs
- command status helpers
- optimistic overlay helpers where declared
- reconnect/refetch helpers
- protocol version checks

Example:

```csharp
await client.Commands.RenameTask(new RenameTaskCommand(taskId, title));

var subscription = await client.Modules.ProjectWorkspace.Subscribe(
    new ProjectWorkspaceParams(projectId),
    cancellationToken);
```

This is the primary client surface. Blazor should use this directly.

### WebAssembly Export Facade

Generated browser-WASM output:

- small `[JSExport]` methods for JavaScript entry points
- coarse-grained command and module calls
- callback registration
- command-status observation
- subscription notification dispatch
- JSON/binary payload boundaries

The facade should be intentionally small because exported methods are part of the browser interop surface and are not where broad domain APIs should live.

Example conceptual exports:

```csharp
[JSExport]
public static Task<string> DispatchCommand(string commandId, string payloadJson);

[JSExport]
public static Task<string> SubscribeModule(string moduleId, string parametersJson);

[JSExport]
public static void Unsubscribe(string subscriptionId);
```

Generated TypeScript wrappers should hide these low-level string/payload methods.

### JavaScript And TypeScript Bindings

Generated JS/TS output:

- `*.d.ts` declarations
- ergonomic JS functions
- command parameter types
- module parameter types
- module node/edge types
- snapshot and patch types
- callback types
- error and command-status types

Example:

```ts
const project = await store.modules.projectWorkspace.subscribe({
  projectId: "p_42"
});

await store.commands.renameTask({
  taskId: "t_1",
  title: "Fix the login redirect",
  expectedVersion: project.sequence
});
```

The TS binding calls the generated C# WASM export facade. It should not call the server directly unless explicitly configured for a no-WASM fallback mode.

## Custom Client-Side C# Code

Developers should be able to write C# code that runs in the browser alongside DataLinq.Store.

Use cases:

- UI orchestration
- command composition
- client-side validation
- derived selectors
- formatting
- workflow state machines
- integration with generated Store APIs

Conceptual sketch:

```csharp
[StoreClientAction]
public static async ValueTask RenameCurrentTask(
    ProjectStoreClient client,
    TaskRef taskId,
    string title,
    CancellationToken cancellationToken)
{
    await client.Commands.RenameTask(
        new RenameTaskCommand(taskId, title),
        cancellationToken);
}
```

Generated TS:

```ts
await store.actions.renameCurrentTask(taskId, title);
```

This is especially useful for normal ASP.NET applications that want rich browser state behavior without adopting Blazor components. The app can use regular HTML/JS/TS while calling into generated C# state logic compiled to WebAssembly.

## Blazor Shape

Blazor does not need the JavaScript facade for normal component code.

Blazor should consume:

- generated C# client proxy
- generated module descriptors
- generated command methods
- Blazor adapter subscriptions
- command status and optimistic overlay state

Conceptual Blazor use:

```csharp
var workspace = await Store.Modules.ProjectWorkspace.Subscribe(
    new(projectId),
    cancellationToken);

await Store.Commands.RenameTask(new(taskId, title));
```

The same generated contract should still work if JavaScript also needs to call exported C# client actions in a hybrid page.

## Server Authorization And Validation

Generated bindings are not security.

The server must remain authoritative for:

- authentication
- authorization
- command validation
- module parameter validation
- tenant boundaries
- module visibility
- mutation permission

Client-side C# can improve ergonomics and early feedback, but it cannot be trusted. Everything crossing from client to server must be treated as hostile.

The full module authorization model lives in [Security and Authorization Model](Security%20and%20Authorization%20Model.md).

## Error Contracts

The generated API should avoid throwing transport-specific exceptions into UI code.

Use stable error shapes:

```text
ValidationError
AuthorizationError
ConflictError
SchemaMismatchError
TransportError
ServerError
UnsupportedClientVersionError
```

Each error should carry:

- stable code
- human-readable message when safe
- correlation id
- command id or subscription id when relevant
- retry/refetch recommendation when relevant

## Versioning

Generated artifacts need compatibility metadata:

- Store protocol version
- module contract version
- command contract version
- generated schema hash
- server schema hash
- minimum compatible client version

On mismatch, the client should fail explicitly. A silent mismatch between generated TS, generated C# WASM, and server endpoints will produce nonsense bugs.

The full identity and protocol compatibility model lives in [Identity, Versioning, and Protocol Compatibility](Identity%20Versioning%20and%20Protocol%20Compatibility.md).

## Transport Choices

The contract should be transport-neutral.

Possible generated transports:

- in-process test transport
- HTTP request/response for commands
- SignalR for subscriptions and patches
- raw WebSocket for a lean protocol
- Server-Sent Events for server-to-client invalidation with HTTP commands

The first implementation can choose one transport, but the contract should not make that choice irreversible.

## AOT And Trimming

Generated bindings must respect the browser/AOT support boundary.

Rules:

- generated serializers should use source-generated metadata
- exported WASM methods should be minimal and explicit
- avoid reflection-based endpoint discovery in the browser path
- avoid dynamic JSON shape discovery in hot paths
- keep analyzer/generator assets out of runtime payload
- do not export every generated method directly to JavaScript

The generated TS facade can be rich. The `[JSExport]` surface should stay small.

## Diagnostics

Generated API diagnostics should include:

- command calls by command id
- module subscriptions by module id
- server roundtrip duration
- transport retries
- generated client/server schema versions
- JS-to-C# calls
- C#-to-server calls
- callback dispatch counts
- command errors by category
- subscription errors by category

These diagnostics are necessary because the call chain can cross JavaScript, WebAssembly, generated C#, transport code, ASP.NET, DataLinq transactions, and back again.

## First Useful Slice

The first useful slice should be small:

1. define `[StoreApi]`, `[StoreCommand]`, `[StateModule]`, and `[StoreClientAction]` marker shapes
2. generate a C# client proxy for one command and one module
3. generate a server ASP.NET adapter for the same contract
4. generate source-generated JSON context
5. generate a minimal WASM export facade with coarse methods
6. generate TypeScript declarations and wrappers
7. run a browser smoke where JS calls a generated C# client action
8. verify the command updates server state and returns a module replacement or patch

Do not start by supporting arbitrary ASP.NET actions or every transport.

## Open Questions

- Should the contract be interface-based, static-method-based, or registration-based?
- Should generated ASP.NET adapters target minimal APIs first or SignalR first?
- Should command and module ids default from method names or require explicit names?
- Should custom client actions be exported individually or routed through one generated dispatch method?
- Should generated TS be emitted by the C# source generator, a build target, or a separate CLI/tooling package?
- Should the first JS facade use JSON strings, JS object marshaling, or a binary payload?
- Should there be a no-WASM fallback where generated TS calls the server directly?
- How should generated bindings integrate with existing ASP.NET auth and antiforgery conventions?
