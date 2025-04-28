# DataLinq

DataLinq is an lightweight, high-performance ORM using source generators that prioritizes data integrity, thread safety, and efficient caching through use of immutable models. It is designed to lean heavily on the cache to trade memory usage for speed.

### Goal
The aim of the library is to minimize memory allocations and maximize speed of data retrieval, with the main focus being on read-heavy applications on small to medium projects. One focus area is to solve the classical N+1 problem, making access to related entities as close as possible to reading a local variable.

### Motivation
DataLinq is an exploration of the idea of combining immutability, indirect querying and caching. It is also an exploration of using source generators to minimize the overhead of reflection usually plaguing ORM libraries.

### Core Philosophy
- **Immutability First:**  
  All data read from the database is represented as immutable objects, ensuring thread safety and predictable behavior. When modifications are required, DataLinq provides an mechanism to create mutable copies, update them, and seamlessly synchronize the changes.
- **Efficient Caching:**  
  The framework leverages both global and transaction-specific caching, dramatically reducing database hits.
- **LINQ Querying:**  
  Built on LINQ, DataLinq translates queries into backend-specific commands without exposing you to the underlying complexities.
- **Minimize boilerplate:**  
  Use the CLI tool to create model classes that defines types and database structure, then DataLinq automatically generate immutable and mutable classes with the built in source generator.
- **Compile time errors:**  
  Move as many errors as possible from runtime to compile time. For example is setting of required fields enforced by the compiler and will give a compile time error, rather than an error when inserting to the database.
- **Extensibility:**  
  Although current support includes MySQL/MariaDB and SQLite, the modular architecture makes it straightforward to extend to other data sources in the future.

---

## Getting Started

### Installation
Install DataLinq via NuGet. These are the currently available backends:

```bash
dotnet add package DataLinq.MySql
dotnet add package DataLinq.SQLite
```

The CLI is installed as a dotnet tool:
```bash
dotnet tool install --global DataLinq.CLI
```

### Configuration
1. **Database Connection:**  
   Configure your connection strings (for MySQL/MariaDB or SQLite) in your application’s configuration file.
2. **DataLinq Configuration:**  
   Use the provided configuration file (e.g., `datalinq.json`) to define your database settings.

### Model Creation
Generate your data models directly from your database schema using the CLI:

```bash
datalinq create-models -n YourDatabaseName
```

And then to generate SQL scripts from the models:

```bash
datalinq create-sql -o output.sql -n YourDatabaseName
```

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
// Retrieve an immutable user
var user = usersDb.Query().Users.Single(u => u.Id == 1);

// Create a mutable copy, update the record, and save changes
var updatedUser = user.Mutate(u => u.Name = "New Name").Save();
```

### Accessing Related Entities
Fetch a department and its associated managers:

```csharp
var department = employeesDb.Query().Departments.Single(d => d.DeptNo == "d005");
var managers = department.Managers;  // Fetches collection of managers from cache
```

---

## Contributing & Further Resources

### Documentation
For in-depth technical details, advanced usage, and troubleshooting, please refer to the [official documentation](docs/index.md).

### Contributing
We welcome contributions from the community! Whether you’re fixing bugs, improving documentation, or adding new features, your help is appreciated. Please see our [Contributing Guide](docs/Contributing.md) for details.

### License
DataLinq is open source and distributed under the MIT License. See the [LICENSE](LICENSE.md) file for more details.