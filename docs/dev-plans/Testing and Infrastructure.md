> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: Test Suite Reorganization, TUnit Migration, and Podman Infrastructure

**Status:** Draft  
**Goal:** Replace the current ad hoc xUnit-based test setup with a staged, TUnit-based test architecture that has clear project boundaries, deterministic database state, and a reproducible Podman-backed environment for local development and CI.

## 1. Current State Audit

The current suite is not merely "old." It is structurally muddled.

### 1.1. Projects Are Mixed by Responsibility

Today the test code is split across:

* `src/DataLinq.Tests`
* `src/DataLinq.MySql.Tests`
* `src/DataLinq.Generators.Tests`

That sounds reasonable until you inspect the contents.

`DataLinq.Tests` currently mixes:

* Pure unit-style tests
* Metadata and parser tests
* Cross-provider integration tests
* SQLite-specific integration tests
* Fixture-driven behavior tests
* At least one suspicious leftover source-generator test file

So the main test project is not a test category. It is a dumping ground.

### 1.2. Database Setup Is Duplicated and Machine-Centric

The current database setup is centered around duplicated fixtures and local config discovery:

* `src/DataLinq.Tests/DatabaseFixture.cs`
* `src/DataLinq.MySql.Tests/DatabaseFixture.cs`

Problems with the current model:

* Fixture logic is duplicated across projects.
* Test config is discovered from `datalinq.json` plus local user overrides.
* Current user config contains machine-specific paths and real credentials.
* Logging paths are hard-coded to Windows-specific absolute paths.
* Database creation, seeding, and cleanup are partially owned by test fixtures instead of a dedicated infrastructure layer.

This is the wrong ownership model. Test methods should verify behavior. They should not quietly act as environment provisioning scripts.

### 1.3. The Suite Depends Heavily on Shared Mutable State

Current xUnit patterns include:

* `IClassFixture<T>`
* Static fixtures
* `MemberData`
* xUnit collection-level serialization
* Assembly-wide parallelization disabled

`src/DataLinq.Tests/TestAssembly.cs` disables parallelization for the entire assembly. That is a symptom, not a strategy.

### 1.4. There Is No Real Test CI Yet

At the time of writing, `.github/workflows/static.yml` only deploys documentation. There is no first-class GitHub Actions workflow for the test suite. Any new plan that talks about CI must acknowledge that we are effectively building test CI from scratch.

---

## 2. What Needs To Change In This Document

The previous draft was directionally fine but practically incomplete. It needs the following corrections.

### 2.1. Replace "Docker Compose" as the Centerpiece

The old draft used `docker-compose.yml` as the primary infrastructure artifact. That is not the right center of gravity if the stated goal is Podman.

We should treat Podman as the first-class runtime and keep orchestration in repo-owned scripts and manifests that call Podman directly.

Why:

* `podman compose` depends on an external compose provider, which adds another moving part.
* The requirement is not "containers somehow." The requirement is a stable, Podman-backed developer and CI workflow.
* A direct `podman` or `podman kube play` workflow is less ambiguous than pretending Docker Compose is the real contract.

### 2.2. Stop Presenting Migration as a Search-and-Replace

The old draft reduced migration to:

* `[Fact]` -> `[Test]`
* `Assert.Equal(...)` -> `await Assert.That(...).IsEqualTo(...)`
* `IClassFixture<T>` -> some TUnit equivalent

That is not enough.

The real migration is:

1. Reorganize the suite by responsibility.
2. Introduce a single database test harness.
3. Move environment provisioning out of fixtures.
4. Establish parity criteria between old and new suites.
5. Only then port tests project by project.

### 2.3. Add a Coexistence Phase

The old draft jumped too quickly to refactoring existing projects. That is risky.

We should explicitly keep the legacy xUnit suite alive while the new TUnit suite is built in parallel. Old tests should only be removed once the new suite proves parity on the same behavior.

### 2.4. Clarify What "All Databases in Podman" Means

Server databases belong in Podman:

* MySQL
* MariaDB

SQLite does not. SQLite should remain file-based or in-memory, managed directly by the tests. Trying to "containerize SQLite" would be performative nonsense.

---

## 3. Recommended Target Architecture

### 3.1. Test Project Layout

The end state should not be one giant replacement test project.

Recommended target shape:

* `src/DataLinq.Tests.Unit`
  Pure library tests with no external database dependency.
* `src/DataLinq.Tests.Integration`
  Provider-behavior and provider-compliance tests across SQLite, MySQL, and MariaDB.
* `src/DataLinq.Generators.Tests`
  Keep as its own project, but migrate it to TUnit separately.
* `src/DataLinq.Testing`
  Shared test infrastructure library for fixtures, provider descriptors, seeding APIs, and Podman-aware helpers.

### 3.2. Transitional Starting Point

For the first migration step, it is reasonable to create a single new project such as:

* `src/DataLinq.Tests.TUnit`

That project should be a proving ground, not the final architecture.

It should contain:

* The first TUnit-based infrastructure abstractions
* A small vertical slice of migrated tests
* Enough coverage to prove Podman setup, deterministic seeding, and TUnit execution work end-to-end

Once that pattern is stable, we should split along the final boundaries above.

### 3.3. Suite Categories We Need Explicitly

The new structure should separate these categories on purpose:

* Pure unit tests
* Metadata/model factory tests
* Provider compliance tests
* Provider-specific schema and metadata tests
* Source-generator tests
* Infrastructure smoke tests

Right now several of those are mixed inside `DataLinq.Tests`, which is exactly why the suite feels messy.

---

## 4. TUnit Strategy

We are migrating to **TUnit** because it is source-generator based and runs on **Microsoft.Testing.Platform**, which fits the project's AOT direction better than xUnit. See the official [TUnit introduction](https://tunit.dev/docs/intro/), [installation guide](https://tunit.dev/docs/getting-started/installation/), and [running tests guide](https://tunit.dev/docs/getting-started/running-your-tests).

### 4.1. Do Not Port xUnit Shapes Blindly

TUnit should not be treated as xUnit with different attribute names.

We should avoid carrying over:

* `object[]`-driven `MemberData` everywhere
* global static fixtures
* broad assembly-wide serialization
* duplicated class fixtures per provider

Instead, the new TUnit suite should use explicit provider descriptors and shared setup objects with clean lifecycle boundaries.

### 4.2. Provider Matrix Should Be Explicit

Many current tests are really compliance tests that happen to be expressed as xUnit `[Theory]` methods over `BaseTests.GetEmployees()`.

That needs a cleaner model in the new suite:

* A provider descriptor that identifies SQLite file, SQLite in-memory, MySQL, and MariaDB
* A shared way to create databases for a given provider
* A clear distinction between provider-agnostic behavior tests and provider-specific tests

This is more important than the mechanical attribute conversion.

### 4.3. Parallelism Should Be Designed, Not Disabled

The new TUnit suite should:

* Allow parallel execution for pure unit tests
* Allow parallel execution for isolated integration tests where possible
* Serialize only the tests that truly share a mutable external resource

If we end up globally disabling parallelism again, then the migration failed architecturally even if the framework changed.

### 4.4. Coexistence on .NET 10 Needs Runner Discipline

During the migration, we will have both legacy xUnit projects and new TUnit projects in the same repository.

On .NET 10, **Microsoft.Testing.Platform** and old VSTest-based flows do not mix cleanly if you try to flip the whole repo over at once.

Practical rule:

* Keep the repo-level runner settings conservative while xUnit still exists.
* Run the new TUnit project directly with `dotnet run` or a project-scoped MTP-compatible invocation.
* Do not switch the entire repository to a global MTP `dotnet test` configuration until the legacy xUnit projects are gone or isolated appropriately.

---

## 5. Podman-Based Infrastructure

We want reproducible server databases without requiring developers to install and configure MySQL or MariaDB locally.

### 5.1. First-Class Runtime

Use **Podman** as the supported local and CI container runtime.

Recommended repository structure:

* `test-infra/podman/`
* `test-infra/podman/up.ps1`
* `test-infra/podman/down.ps1`
* `test-infra/podman/reset.ps1`
* `test-infra/podman/wait.ps1`
* `test-infra/podman/` manifest files if we use `podman kube play`

Podman references:

* [What is Podman?](https://docs.podman.io/en/v4.1.1/)
* [`podman play kube`](https://docs.podman.io/en/v4.0.0/markdown/podman-play-kube.1.html)

### 5.2. Infrastructure Ownership

The infrastructure layer should own:

* Starting MySQL and MariaDB containers
* Naming containers and networks/pods deterministically
* Waiting for readiness
* Exposing stable ports or connection endpoints
* Resetting databases between runs when required
* Tearing the environment down cleanly

Test fixtures should consume a ready environment. They should not be the thing that bootstraps it.

### 5.3. Config and Secrets

We must stop relying on local secrets committed to developer-specific config files.

The new plan should use:

* Test-only usernames and passwords
* Repo-owned example config or generated config
* CI secrets only where genuinely needed

For local and CI test containers, static test credentials are fine if they are only used for ephemeral test databases. That is far better than silently depending on somebody's real local setup.

### 5.4. GitHub Actions

We need a dedicated test workflow, for example:

* `.github/workflows/test.yml`

The workflow should:

1. Check out the repo
2. Install the required .NET SDK
3. Ensure Podman is available
4. Start the test databases via repo-owned scripts
5. Seed/reset databases
6. Run the new TUnit projects
7. Optionally run the legacy xUnit projects during the coexistence phase
8. Upload logs and test reports

This should be built as a real workflow, not implied by documentation prose.

---

## 6. Seeding and Deterministic Data

The current Bogus-based employee seeding lives inside `src/DataLinq.Tests/DatabaseFixture.cs`. That is the wrong place for it.

### 6.1. Extract Shared Seeding Logic

Create:

* `src/DataLinq.Seeding`
  Shared library containing deterministic schema bootstrap and data generation logic
* `src/DataLinq.Seeder`
  Thin CLI wrapper for local setup and CI orchestration

This is better than a CLI-only approach because tests should be able to call the shared logic directly when appropriate.

### 6.2. Determinism Rules

The seeding layer must:

* Use a fixed seed
* Generate the same dataset every run
* Be idempotent or reset-driven
* Be explicit about scenario names and row counts

If the data changes, it should change because we changed the seed or scenario intentionally, not because a fixture got clever.

### 6.3. Test Scenarios

At minimum we should support:

* `employees-default`
* `employees-edge-cases`
* `metadata-temp-schema`

The first scenario supports most provider-behavior tests. The others support targeted cases without polluting the default seed dataset.

---

## 7. Proposed Migration Plan

This should be done in phases. Trying to port everything in one pass would be reckless.

### Phase 0: Audit and Freeze the Legacy Baseline

Deliverables:

* `docs/dev-plans/Test Suite Audit.md`
* Inventory the current test files and categorize them
* Identify obviously dead, duplicate, or misplaced tests
* Record current pass/fail status for each existing test project
* Define the list of behaviors the new suite must preserve

Notes:

* This is where we decide what the legacy suite actually covers.
* We should not assume that every current test is valuable or even correct.

### Phase 1: Build the New Infrastructure Layer

Deliverables:

* Podman scripts and manifests under `test-infra/podman/`
* Stable local test credentials and endpoints
* `DataLinq.Seeding` library
* `DataLinq.Seeder` CLI
* A shared `DataLinq.Testing` library

Exit criteria:

* A developer can provision MySQL and MariaDB with one command
* CI can do the same without manual setup
* Databases can be reset deterministically

### Phase 2: Create the First TUnit Project

Deliverables:

* A new TUnit test project, initially `src/DataLinq.Tests.TUnit`
* Basic TUnit wiring and package references
* Shared provider descriptors and setup types
* A small set of smoke tests across SQLite, MySQL, and MariaDB

Suggested initial test slice:

* One CRUD behavior test
* One transaction behavior test
* One metadata parsing test
* One provider-specific test

Why:

* This proves the framework, the infra, and the test-harness design together.
* If this step is ugly, the broader migration will be worse.

### Phase 3: Migrate by Category, Not File Name

Migration order should be:

1. Pure unit tests
2. Metadata/model factory tests
3. Cross-provider behavior/compliance tests
4. Provider-specific MySQL/MariaDB tests
5. Source-generator tests

This order matters.

It is stupid to begin with the messiest provider-specific integration tests before the shared harness and test style are stable.

### Phase 4: Run Old and New Side by Side

During the coexistence phase:

* Keep legacy xUnit projects in the repo
* Keep legacy projects runnable
* Run both old and new suites in CI where feasible
* Track migrated coverage explicitly

Recommended artifact:

* A parity checklist mapping old test classes or behaviors to new TUnit replacements

### Phase 5: Cut Over and Remove Legacy xUnit

Only after parity is proven should we:

* Remove xUnit package references
* Remove old xUnit-only fixtures
* Remove old projects that have been fully replaced
* Rename transitional projects if needed

---

## 8. Parity Rules Before Deleting the Old Suite

We should not remove the legacy tests until all of the following are true:

* The new TUnit suite covers the same important behaviors as the old suite
* Provider behavior is validated across SQLite, MySQL, and MariaDB where intended
* Provider-specific tests still exist where behavior genuinely differs
* The new Podman workflow works locally
* The new Podman workflow works in GitHub Actions
* The new suite is stable across repeated runs

A framework migration without explicit parity criteria is how regressions get smuggled in under the banner of cleanup.

---

## 9. Practical Suggestions

### 9.1. Yes, Create a New Test Project First

That part of your instinct is right.

We should create a new TUnit-first project and prove the pattern before touching the legacy projects too aggressively.

### 9.2. No, Do Not Immediately Delete or Rename the Old Projects

That would collapse audit, migration, and validation into one risky step.

Keep the old suite intact while the new suite grows beside it.

### 9.3. Do Not Start With Assertion Conversion

The first work should be:

* environment ownership
* fixture ownership
* provider matrix design
* project boundaries

Changing assertion syntax before that is cosmetic work on top of unstable architecture.

### 9.4. Treat MySQL and MariaDB Tests as Real Integration Tests

Some of the current MySQL and MariaDB tests create databases, tables, and schema variations dynamically. That is fine, but it means they belong in a clearly marked integration or provider-specific lane. They should not be mixed with pure unit tests.

---

## 10. Immediate Next Steps

1. Create a concrete inventory of the current suite grouped into:
   * pure/unit
   * cross-provider integration
   * SQLite-specific
   * MySQL/MariaDB-specific
   * generator
2. Design the `DataLinq.Testing` shared infrastructure API.
3. Decide whether Podman orchestration will use direct `podman` commands or `podman kube play`.
4. Create the first TUnit project and migrate a thin vertical slice.
5. Add a dedicated GitHub Actions test workflow.
6. Only after that, start broad migration of legacy xUnit tests.

## 11. Summary

The old draft was too optimistic. The hard part is not "convert xUnit to TUnit." The hard part is untangling test responsibilities, centralizing infrastructure, and proving parity without breaking coverage.

The right approach is:

* new TUnit suite first
* old xUnit suite kept alive temporarily
* Podman-owned server DB infrastructure
* deterministic seeding in a shared library
* migration by category
* cutover only after parity is proven
