# DataLinq

DataLinq is a lightweight, high-performance ORM that prioritizes data integrity, thread safety, and efficient caching through an innovative use of immutable models. It is designed to minimize memory allocations and speed up data retrieval, making it ideal for read-heavy applications on small to medium projects.

---

## 1. Introduction & Overview

### What Is DataLinq?
DataLinq is a modern ORM framework built around the principles of immutability and minimal overhead. By automatically generating both immutable and mutable model classes from your database schema, it ensures consistency and performance while simplifying data access.

### Core Philosophy
- **Immutability First:**  
  All data read from the database is represented as immutable objects, ensuring thread safety and predictable behavior. When modifications are required, DataLinq provides an easy-to-use mechanism to create mutable copies, update them, and seamlessly synchronize the changes.
- **Efficient Caching:**  
  The framework leverages both global and transaction-specific caching, dramatically reducing database hits and improving response times.
- **Unified Querying:**  
  Built on LINQ, DataLinq translates expressive, familiar queries into optimized backend-specific commands without exposing you to the underlying complexities.
- **Extensibility:**  
  Although current support includes MySQL/MariaDB and SQLite, the modular architecture makes it straightforward to extend to other data sources in the future.


## 3. Getting Started

### Installation
Install DataLinq via NuGet. These are the currently available backends:

```bash
Install-Package DataLinq.MySql
Install-Package DataLinq.SQLite
```

The CLI is installed as a dotnet tool:
```bash
dotnet tool install --global DataLinq.CLI
```

### Configuration
1. **Database Connection:**  
   Configure your connection strings (for MySQL/MariaDB or SQLite) in your application’s configuration file.
2. **DataLinq Configuration:**  
   Use the provided configuration file (e.g., `datalinq.json`) to define your database settings and cache options.

### Model Creation
Generate your data models directly from your database schema using the CLI:

```bash
datalinq create-models -n YourDatabaseName
```

Or, if you prefer generating SQL scripts for database setup:

```bash
datalinq create-sql -o output.sql -n YourDatabaseName
```

---

## 4. Code Examples & Usage

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

### Using the CLI
Create a new database from your models via the command line:

```bash
datalinq create-database -n YourDatabaseName
```

---

## 5. How It Works (Advanced Overview)

### Source Generation & Metadata
DataLinq scans your abstract model classes—annotated with attributes such as `[Table]`, `[Column]`, and `[Relation]`—to automatically generate consistent immutable and mutable classes. This approach minimizes boilerplate and ensures that your data models always reflect the underlying database schema.

### Caching Strategy
- **Global Cache:**  
  Immutable objects are cached globally to enable zero-allocation reads.
- **Transactional Cache:**  
  During data mutations, a dedicated transactional cache maintains consistency until changes are committed.
- **Cache Invalidation:**  
  Supports multiple strategies including automatic updates on mutation, manual refreshes, time-based expiry, and event-driven notifications.

### Query Translation
LINQ expressions are parsed and optimized before being converted into backend-specific commands. This translation layer allows you to write complex queries in a concise manner without worrying about the underlying SQL syntax.

---

## 6. Contributing & Further Resources

### Contributing
We welcome contributions from the community! Whether you’re fixing bugs, improving documentation, or adding new features, your help is appreciated. Please see our [Contributing Guide](docs/Contributing.md) for details.

### Documentation & Support
For in-depth technical details, advanced usage, and troubleshooting, please refer to the [official documentation](docs/Index.md).

### License
DataLinq is open source and distributed under the MIT License. See the [LICENSE](LICENSE) file for more details.