# Querying

DataLinq’s querying interface is built on a simple, intuitive, and high-performance foundation. In this chapter, you will learn how to set up your database connection, execute basic queries using LINQ, and leverage additional query syntaxes available in DataLinq.

## 1. Database Setup

When using DataLinq, configuration via JSON files is only required for the CLI tool. At runtime, you set up your ORM using a connection string. For example, you might initialize your database as follows:

```csharp
using DataLinq;
using DataLinq.Tests.Models.Employees;

// Provide your connection string (this example uses MySQL)
var connectionString = "server=localhost;user=root;database=employees;password=yourpassword;";

// Instantiate your strongly-typed database object.
var db = new MySqlDatabase<EmployeesDb>(connectionString);
```

This creates an instance of your database (here, `EmployeesDb`), which exposes properties for accessing tables and views through strongly typed collections.

---

## 2. Basic LINQ Query Syntax

DataLinq’s primary querying interface is built on standard LINQ expressions. Currently, only basic methods have been implemented, and these are parsed and translated into efficient SQL commands. The supported LINQ methods include:

### Where

**Purpose:** Filters records based on a Boolean condition.  
**Example:**

```csharp
// Retrieve all employees with the name "John"
var johns = db.Query().Employees
    .Where(e => e.Name == "John")
    .ToList();
```

In this example, the `Where` method filters the `Employees` collection so that only records where the `Name` property equals "John" are returned.

### OrderBy

**Purpose:** Sorts records based on a specified key in ascending order.  
**Example:**

```csharp
// Retrieve employees ordered by their ID in ascending order
var orderedEmployees = db.Query().Employees
    .OrderBy(e => e.Id)
    .ToList();
```

Here, `OrderBy` instructs DataLinq to generate an SQL ORDER BY clause using the `Id` column, ensuring that the returned employees are sorted by their IDs.

### Skip and Take

**Purpose:** Implements pagination by skipping a specified number of records and then taking a defined number of subsequent records.  
**Example:**

```csharp
// Retrieve the second page of results, where each page contains 10 records.
var secondPage = db.Query().Employees
    .OrderBy(e => e.Id)
    .Skip(10)   // Skip the first 10 records.
    .Take(10)   // Take the next 10 records.
    .ToList();
```

This combination of `Skip` and `Take` lets you control which subset of records is returned, useful for paginating large result sets.

### First

**Purpose:** Retrieves the first element in a sequence.  
**Example:**

```csharp
// Retrieve the first employee in the ordered list.
var firstEmployee = db.Query().Employees
    .OrderBy(e => e.Id)
    .First();
```

This method returns the first employee according to the ordering provided. Note that if no record exists, an exception will be thrown.

### Single

**Purpose:** Retrieves a unique element from the sequence; it throws an exception if more than one element matches or if none are found.  
**Example:**

```csharp
// Retrieve the employee with a specific ID.
var uniqueEmployee = db.Query().Employees
    .Where(e => e.Id == 1)
    .Single();
```

Here, `Single` ensures that exactly one employee with `Id == 1` exists; if the condition matches zero or multiple records, an exception will be raised.

### ToList

**Purpose:** Executes the query and materializes the results as a list of immutable objects.  
**Example:**

```csharp
// Execute the query and convert the result set into a list.
var employeeList = db.Query().Employees.ToList();
```

Calling `ToList` forces the execution of the query, causing DataLinq to fetch the data from the database (or cache) and convert the result into a list for further processing.

---

For further technical details on how these expressions are parsed and translated, please refer to the [Query Translator](Query%20Translator.md) documentation  and the associated test cases in the repository. These examples provide a clear understanding of the current capabilities, while advanced features such as projection, grouping, and joins are planned for future releases.

## 3. Lazy Loading of Foreign Key Relations

DataLinq supports lazy loading for foreign key relationships. When you access a navigation property that represents a foreign key relation, the ORM automatically triggers a lazy load. The first time you access the property, DataLinq fetches the related record from the database and caches the result. Subsequent accesses return the cached object, reducing redundant database calls and ensuring efficient data retrieval.

Tests in the repository illustrate this behavior by checking that the foreign key property is only loaded on demand. This approach ensures that related data is available when needed without incurring the cost of loading all relationships upfront.

## 4. Alternative Query Syntaxes

In addition to the standard LINQ interface, DataLinq provides alternative ways to query data that do not depend on LINQ.

TODO

## 5. Summary

DataLinq offers a straightforward and performant querying experience:
- **Setup:** Initialize your ORM with a connection string (JSON files are used only by the CLI tool).
- **LINQ Queries:** Use simple LINQ expressions to filter and order data.
- **Lazy Loading:** Access foreign key relations on demand; the first access loads and caches the related object.
- **Alternative Queries:** Choose from a SQL string–based query interface or instantiate immutable objects directly from raw SQL queries.

These various approaches allow you to select the method that best fits your development style while ensuring high performance and minimal overhead.
