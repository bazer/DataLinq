# Specification: Testing Infrastructure and TUnit Migration

**Status:** Draft
**Goal:** Modernize the testing environment to support multiple backends (MySQL, MariaDB, SQLite, Memory) with deterministic data, containerized infrastructure, and a Native AOT-compatible test framework.

---

## 1. The Test Framework: TUnit

We are migrating from **xUnit** to **TUnit** to align with DataLinq's goal of Native AOT and WebAssembly support.

### 1.1. Rationale
*   **AOT Compatibility:** TUnit uses Source Generators for test discovery, eliminating runtime reflection. This proves DataLinq's AOT capabilities during the test phase.
*   **Microsoft Testing Platform:** Built on the modern MTP stack, ensuring future compatibility with VS and CLI tooling.

### 1.2. Migration Strategy
*   **Attributes:** `[Fact]` -> `[Test]`.
*   **Assertions:** `Assert.Equal(a, b)` -> `await Assert.That(a).IsEqualTo(b)`.
*   **Lifecycle:** `IClassFixture<T>` -> TUnit `[ClassDataSource]` or shared instances via Dependency Injection logic provided by TUnit.

---

## 2. The Compliance Suite (TCK)

To ensure the **In-Memory**, **SQLite**, and **MySQL** providers behave identically, we will implement a **Technology Compatibility Kit (TCK)** pattern.

### 2.1. Abstract Test Classes
Tests are written *once* against an interface.

```csharp
// DataLinq.Tests.Core/Compliance/CrudTests.cs
public abstract class CrudTests<TFixture> where TFixture : IDatabaseFixture, new()
{
    protected TFixture Fixture { get; } = new();

    [Test]
    public async Task Can_Insert_And_Read_Employee()
    {
        var db = Fixture.GetDatabase();
        // ... test logic ...
        await Assert.That(result).IsNotNull();
    }
}
```

### 2.2. Concrete Implementations
Each provider implements the fixture and inherits the suite.

```csharp
// DataLinq.MySql.Tests/MySqlCrudTests.cs
public class MySqlCrudTests : CrudTests<MySqlDatabaseFixture> 
{ 
    // TUnit automatically discovers the inherited [Test] methods
}
```

---

## 3. Infrastructure: Docker Compose

We will not rely on locally installed databases.

**`docker-compose.yml`** (Root of repository):
```yaml
services:
  # MySQL 8.0
  datalinq-mysql:
    image: mysql:8.0
    environment:
      MYSQL_ROOT_PASSWORD: password
      MYSQL_DATABASE: employees
    ports:
      - "3307:3306"
    tmpfs:
      - /var/lib/mysql # Run in RAM for speed, no persistence needed between runs

  # MariaDB (Latest)
  datalinq-mariadb:
    image: mariadb:latest
    environment:
      MARIADB_ROOT_PASSWORD: password
      MARIADB_DATABASE: employees
    ports:
      - "3308:3306"
    tmpfs:
      - /var/lib/mysql
```

---

## 4. Data Seeding: `DataLinq.Seeder`

We will extract the `Bogus` logic from `DatabaseFixture` into a standalone CLI tool. This allows developers to reset their environment without running tests.

### 4.1. Tool Specification
*   **Project:** `src/DataLinq.Seeder` (Console App)
*   **Arguments:**
    *   `-c, --connection`: Connection String.
    *   `-t, --type`: Database Type (MySQL, SQLite).
    *   `-s, --scenario`: "Default", "Benchmark", "EdgeCases".

### 4.2. Determinism
The seeder **must** set a static seed for Bogus (`Randomizer.Seed = 12345`). This ensures that "Alice Smith" is generated with the same birthdate every single time, eliminating "flaky tests" caused by random data edge cases.

---

## 5. Developer Workflow

### 5.1. Setup Script (`setup-test-env.ps1`)
A script to orchestrate the environment.

```powershell
# 1. Start Containers
docker-compose up -d

# 2. Wait for Ports (3307, 3308) to accept connections

# 3. Run Seeder
dotnet run --project src/DataLinq.Seeder -- -t MySQL -c "..." -s Default
dotnet run --project src/DataLinq.Seeder -- -t MariaDB -c "..." -s Default

# 4. Ready
Write-Host "Environment ready for TUnit."
```

### 5.2. CI/CD Integration
GitHub Actions can simply run `docker-compose up -d` and the Seeder before executing `dotnet test`.

## 6. Implementation Steps

1.  [ ] Create `docker-compose.yml` and `setup-test-env.ps1`.
2.  [ ] Extract Bogus logic into `DataLinq.Seeder` project.
3.  [ ] Add **TUnit** dependency to test projects.
4.  [ ] Create `IDatabaseFixture` interface.
5.  [ ] Refactor existing `DataLinq.Tests` into `DataLinq.Tests.Core` (Abstract TCK) and `DataLinq.SQLite.Tests` (Concrete).
6.  [ ] Migrate assertions to TUnit syntax.