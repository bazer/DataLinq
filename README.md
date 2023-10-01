
# DataLinq

DataLinq is a state-of-the-art database interfacing library uniquely designed with an emphasis on immutability. This approach ensures data integrity, consistency, and superior performance. Whether you're performing basic CRUD actions, handling complex relationships, caching data, executing parallel processes, or utilizing the project's Command-Line Interface (CLI), DataLinq streamlines your operations with unmatched efficiency.

## Getting Started

Starting with DataLinq is a breeze. Here's a step-by-step guide to get you up and running:

1. **Installation**: Begin by installing the DataLinq package via your package manager.
2. **Configuration**: Set up your database connection details. Currently, DataLinq supports MySQL (including MariaDB) and SQLite. Support for additional database engines is planned for future releases.
3. **Model Creation**: Utilize DataLinq's CLI or API to generate data models based on your database schema.
4. **Operations**: Dive into querying, CRUD operations, relationship management, and more using DataLinq's intuitive methods.

For a detailed guide, refer to the [official documentation](#).

## Key Features

- **Metadata Handling**: Seamlessly retrieve and manage database metadata. DataLinq intelligently interprets metadata to facilitate operations like relationship management and caching.
- **Query Operations**: Perform SQL-like query operations with ease. Methods such as `ToList()`, `Count()`, `Where()`, and more simplify your querying process. For example:

    ```csharp
    var activeUsers = usersDb.Query().Users.Where("status").EqualTo("active").ToList();
    ```

- **Relationship Management**: Effortlessly manage table relationships. DataLinq supports features like lazy loading, ensuring efficient data retrieval without overloading your system.

    ```csharp
    var department = employeesDb.Query().Departments.Single(x => x.DeptNo == "d005");
    Assert.NotEmpty(department.Managers);
    ```

- **Caching**: Enhance performance with built-in caching mechanisms. DataLinq's unique approach to immutable data allows for efficient caching, reducing database hits and ensuring swift data retrieval.

- **Threading Support**: Execute concurrent database operations with DataLinq's robust parallel processing capabilities. This ensures that your application remains responsive even under heavy load.

- **Command-Line Interface (CLI)**: Interact with DataLinq directly from the command line. Use commands like `create-database`, `create-sql`, and `create-models` to manage your database setup.

## Handling Immutable and Mutable Data

DataLinq's philosophy revolves around the principle of immutability, ensuring data consistency and reducing potential pitfalls in application development. With immutable data, once a record is created, it remains unchanged. However, when updates are needed, DataLinq provides a seamless transition from immutable to mutable data, ensuring flexibility without compromising integrity.

### Immutable Data

- **Immutable By Default**: All data loaded from the database is inherently immutable. This ensures data integrity, consistency, and thread safety across the application.
- **Immutable Checks & Caching**: Verify a model's immutability using the `IsImmutable` extension. Given their unchangeable nature, immutable objects are cached efficiently, ensuring faster data retrieval and reducing database overhead.
- **Mutation**: The `Mutate()` extension facilitates transitions. When an update is needed, retrieve a mutable copy of the immutable model, make modifications, and save it back to the database.

### Mutable Data

- **Mutable When Needed**: While DataLinq emphasizes immutability, it recognizes the necessity of change. Models can be made mutable when updates are required.
- **Mutable Checks & Database Operations**: Check a model's mutability using the `IsMutable` extension. Direct database operations, such as `Insert()`, `Update()`, and `Delete()`, can be performed on mutable models.
- **Change Detection**: Before committing changes, review them with the `GetChanges()` method. This ensures that only intended modifications are saved.

## Caching and Performance

DataLinq's caching mechanism is intricately tied to its emphasis on immutability. By leveraging immutable data, the system ensures efficient caching, which in turn, boosts performance.

### Database & Table-Level Caching

- **Efficient Data Retrieval**: DataLinq employs both database and table-level caching. The former manages cache for the entire database, while the latter focuses on individual tables. Together, they ensure swift data retrieval without repeated database hits.
- **Thread Safety**: With the use of concurrent data structures, DataLinq's caching mechanism is thread-safe. This ensures data consistency even in high-concurrency scenarios.
- **Cache Management**: Periodic cache cleanups are performed to ensure optimal memory usage. Old and redundant data is efficiently pruned, ensuring the cache remains lean and efficient.

### Performance Enhancements

By reducing direct database interactions and leveraging cached data, DataLinq significantly reduces application latency. Whether you're querying data, managing relationships, or performing CRUD operations, expect swift responses and a streamlined user experience.

## Advanced Features

DataLinq is not just about basic database operations. Beneath the surface, it employs advanced techniques and mechanisms that set it apart.

### Dynamic Proxy Mechanism

One of DataLinq's standout features is its use of dynamic proxies for data models. This mechanism allows DataLinq to intercept calls to the models, enabling unique functionalities like immutability and change tracking. Whether you're working with an immutable model and transitioning to a mutable one or vice versa, the dynamic proxy mechanism ensures a seamless experience.

### Integrated Caching & Parallel Processing

DataLinq's approach to caching is deeply integrated with its parallel processing capabilities. By caching immutable data, the system can efficiently handle concurrent operations without the risk of data corruption. This ensures that your application remains responsive and data-consistent even under heavy load.

## Querying

DataLinq simplifies the querying process with a SQL-like interface. Whether you're fetching a single record or filtering through thousands, DataLinq's methods make it straightforward.

```csharp
// Fetch all records from the 'Users' table
var allUsers = usersDb.Query().Users.ToList();

// Filter records based on conditions
var activeUsers = usersDb.Query().Users.Where("status").EqualTo("active").ToList();
```

For complex queries and advanced filtering options, refer to the [querying guide](#).

## Relationship Operations

Handling relationships between tables is a central feature of DataLinq. From one-to-many to many-to-many relations, DataLinq has got you covered.

```csharp
// Fetch a department and its associated managers
var department = employeesDb.Query().Departments.Single(x => x.DeptNo == "d005");
var associatedManagers = department.Managers;
```

For more on relationship operations, including lazy loading and eager loading, check out the [relationships guide](#).

## CLI Operations

DataLinq's Command-Line Interface (CLI) is a powerful tool that lets you manage your database setup, generate models, and more, all from the command line.

- `datalinq create-database`: Set up a new database.
- `datalinq create-sql -o [output_file]`: Generate SQL scripts.
- `datalinq create-models`: Produce data models based on your database schema.
- `datalinq list`: View all available databases in your configuration.

For a comprehensive list of CLI commands and their usage, refer to the [CLI documentation](#).

## Testing

Ensuring DataLinq's reliability and robustness is paramount. The library comes equipped with a comprehensive set of unit tests that cover a wide range of scenarios, from basic CRUD operations to complex relationship handling and concurrency tests. Before any release, these tests are rigorously executed to guarantee that DataLinq remains bug-free and performs optimally. Users can also run these tests to verify the library's compatibility with specific environments or configurations.

## Contributing

DataLinq thrives on community contributions. Whether you're a seasoned developer or someone just starting out, your input is valuable. We welcome:

- Code contributions: Enhancements, bug fixes, or new features.
- Documentation: Updates, corrections, or translations.
- Feature suggestions: Ideas for new functionalities or improvements.

Before making a contribution, please check out our [Contributing Guide](#) for guidelines and best practices.

## License

DataLinq is licensed under the MIT License. This license allows for free use, modification, and distribution of the software, provided that the copyright notice and disclaimer are retained. For more details and the full license text, refer to the [LICENSE](#) file.
