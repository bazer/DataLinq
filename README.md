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

For a more complete explanation of config discovery, overrides, and provider selection, see [Configuration Files](docs/Configuration%20files.md).

### Model Creation
Generate your data models directly from your database schema using the CLI:

```bash
datalinq create-models -n YourDatabaseName
```

And then to generate SQL scripts from the models:

```bash
datalinq create-sql -o output.sql -n YourDatabaseName
```

If your config contains more than one database, pass `-n`.
If the selected database contains more than one connection type, pass `-t`.

---

## Code Examples & Usage

### Performing a Simple Query
Retrieve all active users using LINQ:

```csharp
var activeUsers = usersDb.Query().Users
    .Where(x => x.Status == UserStatus.Active)
    .ToList();
```

### Updating Data with Immutability
Fetch an immutable record, mutate it, and then save the changes:

```csharp
var user = usersDb.Query().Users.Single(u => u.Id == 1);
var updatedUser = user.Mutate(u => u.Name = "New Name").Save();
```

### Accessing Related Entities
Fetch a department and its associated managers:

```csharp
var department = employeesDb.Query().Departments.Single(d => d.DeptNo == "d005");
var managers = department.Managers;
```

---

## Contributing & Further Resources

### Documentation
Start with the [documentation index](docs/index.md). The most useful entry points right now are:

- [CLI Documentation](docs/CLI%20Documentation.md)
- [Configuration Files](docs/Configuration%20files.md)
- [Querying](docs/Querying.md)
- [Supported LINQ Queries](docs/Supported%20LINQ%20Queries.md)
- [Caching and Mutation](docs/Caching%20and%20Mutation.md)
- [Transactions](docs/Transactions.md)
- [Attributes and Model Definitions](docs/Attributes%20and%20Model%20Definitions.md)
- [Troubleshooting](docs/Troubleshooting.md)
- [MySQL & MariaDB Provider Notes](docs/backends/MySQL-MariaDB.md)
- [SQLite Provider Notes](docs/backends/SQLite.md)

### Contributing
We welcome contributions from the community. Please see our [Contributing Guide](docs/Contributing.md) for details.

### License
DataLinq is open source and distributed under the MIT License. See the [LICENSE](LICENSE.md) file for more details.
