# DataLinq

DataLinq is a lightweight, high-performance ORM that uses source generators and immutable models to prioritize data integrity, thread safety, and efficient caching. It is designed to lean heavily on the cache to trade memory usage for speed.

### Goal
The aim of the library is to minimize memory allocations and maximize speed of data retrieval, with the main focus being on read-heavy applications on small to medium projects. One focus area is to solve the classical N+1 problem, making access to related entities as close as possible to reading a local variable.

### Motivation
DataLinq is an exploration of combining immutability, indirect querying, and caching. It is also an exploration of using source generators to minimize the reflection overhead that usually plagues ORM libraries.

### Core Philosophy
- **Immutability First:**  
  All data read from the database is represented as immutable objects, ensuring thread safety and predictable behavior. When modifications are required, DataLinq provides a mechanism to create mutable copies, update them, and synchronize the changes.
- **Efficient Caching:**  
  The framework leverages both global and transaction-specific caching, dramatically reducing database hits.
- **LINQ Querying:**  
  Built on LINQ, DataLinq translates supported query shapes into backend-specific commands without exposing you to the underlying complexities.
- **Minimize boilerplate:**  
  Use the CLI tool to create model classes that define types and database structure, then let DataLinq generate immutable and mutable classes with the built-in source generator.
- **Compile time errors:**  
  Move as many errors as possible from runtime to compile time. For example, required fields can be enforced by the compiler instead of failing only when inserting into the database.
- **Extensibility:**  
  Although current support includes MySQL/MariaDB and SQLite, the modular architecture makes it possible to extend to other SQL-like data sources.

---

## Getting Started

### Installation
Install the provider package that matches your runtime database:

```bash
# MySQL and MariaDB
dotnet add package DataLinq.MySql

# SQLite
dotnet add package DataLinq.SQLite
```

The CLI is installed as a dotnet tool named `datalinq`:

```bash
dotnet tool install --global DataLinq.CLI
```

Current package and repo builds target .NET 8, .NET 9, and .NET 10.

### Configuration
The CLI reads `datalinq.json` and, if present next to it, `datalinq.user.json`.

Minimal example:

```json
{
  "Databases": [
    {
      "Name": "AppDb",
      "CsType": "AppDb",
      "Namespace": "MyApp.Models",
      "SourceDirectories": [ "Models/Source" ],
      "DestinationDirectory": "Models/Generated",
      "Connections": [
        {
          "Type": "MariaDB",
          "DataSourceName": "appdb",
          "ConnectionString": "Server=localhost;Database=appdb;User ID=app;Password=secret;"
        }
      ]
    }
  ]
}
```

Generate your data models directly from your database schema:

```bash
datalinq create-models -n AppDb
```

If your config contains more than one database, pass `-n`.
If the selected database contains more than one connection type, pass `-t`.

---

## Code Example

```csharp
var db = new MySqlDatabase<AppDb>(connectionString);

var activeUsers = db.Query().Users
    .Where(x => x.IsActive)
    .ToList();

var user = db.Query().Users.Single(x => x.UserId == userId);
var updatedUser = user.Mutate(x => x.DisplayName = "Updated Name").Save();
```

---

## Documentation

If you want the website-first docs experience, start here:

- [Website Home](home.md)
- [Docs Intro](docs/index.md)
- [Installation](docs/getting-started/Installation.md)
- [Configuration and Model Generation](docs/getting-started/Configuration%20and%20Model%20Generation.md)
- [Your First Query and Update](docs/getting-started/Your%20First%20Query%20and%20Update.md)

After that, the deeper working docs are:

- [Querying](docs/Querying.md)
- [Caching and Mutation](docs/Caching%20and%20Mutation.md)
- [Supported LINQ Queries](docs/Supported%20LINQ%20Queries.md)
- [Transactions](docs/Transactions.md)
- [Attributes and Model Definitions](docs/Attributes%20and%20Model%20Definitions.md)
- [Troubleshooting](docs/Troubleshooting.md)

### License
DataLinq is open source and distributed under the MIT License. See the [LICENSE](LICENSE.md) file for more details.
