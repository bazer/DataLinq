# Caching & Mutation Strategies

DataLinq’s design is built around maximizing read performance and ensuring consistency in a concurrent environment. This is achieved by splitting data access into two distinct phases—query translation with selective data fetching, and subsequent caching/mutation workflows. In this document, we explain how DataLinq optimizes queries by first retrieving primary keys only, then fetching missing rows in bulk; how relation caches are built and maintained; and we provide detailed examples covering various mutation scenarios, including transactions with commits and rollbacks.

---

## 1. Query Execution & Selective Data Fetching

### Primary Key-First Querying

When you execute a LINQ query (e.g. using standard LINQ expressions), DataLinq translates it in two stages:

1. **Primary Key Extraction:**  
   The query is first executed to return only the primary keys of the matching rows. This lightweight step reduces overhead by avoiding the full object materialization if parts of the data are already cached.

2. **Cache Check & Bulk Fetching:**  
   With the list of primary keys in hand, the system checks the Global Cache.  
   - **Cached Rows:**  
     Rows already present are returned directly.
   - **Missing Rows:**  
     If some rows are not in the cache, DataLinq fetches them in a single bulk call from the database. These new rows are then added to the cache so that subsequent requests benefit from the cached data.

This two-step approach ensures that even complex queries incur minimal overhead by avoiding redundant data loads.

### Relation Cache Mechanics

DataLinq maintains an index for foreign key relations, effectively building a relation cache. Here’s how it works:
- **Indexing Foreign Keys:**  
  For each relation, an index is created that maps a foreign key to its corresponding primary keys. This index enables rapid resolution of related entities by simply looking up the primary key(s) in the relation cache.
- **Dynamic Updates:**  
  Whenever data mutations occur (inserts, updates, or deletes), the relation cache is updated automatically. This means that even if a mutation occurs within a transaction, related collections are refreshed so that navigating relationships always returns the current state of the data.

These mechanisms are critical for scenarios where related data is frequently accessed, and they ensure that even after mutations, the caches remain consistent with the underlying database .

---

## 2. Mutation Workflow

### Immutable by Default, Mutable on Demand

All records are initially loaded as immutable objects for safety and performance. When a change is needed, you call the `Mutate()` method. This creates a mutable copy with the following steps:

1. **Conversion to Mutable:**  
   The immutable object is converted into a mutable instance. For example, if required properties are specified, the mutation process enforces that they are provided.

2. **Performing Mutations:**  
   Changes can be made on the mutable object using standard property setters. DataLinq supports not only simple property changes but also updates where some properties are required for the mutation to be valid.

3. **Transactional Updates:**  
   The mutable object is then committed within a transaction:
   - **Commit:**  
     On a successful transaction, a new immutable instance is created from the updated mutable data. This new instance is added to the Global Cache, replacing the outdated version.
   - **Rollback:**  
     If an error occurs during the transaction, the mutation is discarded and the original immutable object remains unchanged.

### Automatic Maintenance of Relation Collections

One key advantage of DataLinq is that related collections (such as a department’s list of managers) are automatically kept up to date:
- **Within Transactions:**  
  Even if mutations occur as part of a larger transaction, the relation caches are updated so that any subsequent query on the same entity returns the latest set of related entities.
- **Post-Mutation Synchronization:**  
  Once a mutation is committed, the system updates both the Global Cache and any relevant relation indices, ensuring that all relationships reflect the new state.

---

## 3. Practical Code Examples

### Example 1: Simple Mutation and Update

```csharp
// Retrieve an immutable record from the cache or database
var user = usersDb.Query().Users.Single(u => u.Id == 1);

// Create a mutable copy with updated required properties (if any)
var mutableUser = user.Mutate(u => u.Name = "Updated Name");

// Commit the mutation within a transaction, updating the cache
var updatedUser = mutableUser.Save();
```

### Example 2: Inserting a New Record with Required Properties

```csharp
// For inserting, the mutable object can be initialized with required properties
var newUser = new MutableUser(requiredProperty1, requiredProperty2);
newUser.Name = "New User";
newUser.Email = "new.user@example.com";

// Insert the new record and commit the transaction
var insertedUser = newUser.Insert();
```

### Example 3: Transaction with Multiple Mutations, Commit & Rollback

```csharp
// Start a transaction
using (var transaction = usersDb.BeginTransaction())
{
    try
    {
        // Update an existing user
        var user = usersDb.Query().Users.Single(u => u.Id == 1);
        var updatedUser = user.Mutate(u => u.Name = "Transactional Name").Save(transaction);

        // Insert a new record within the same transaction
        var newUser = new MutableUser(requiredProperty1, requiredProperty2)
        {
            Name = "New Transaction User",
            Email = "txn.user@example.com"
        };
        var insertedUser = newUser.Insert(transaction);

        // The relation cache for associated entities (like user orders) is updated automatically

        // Commit the transaction to apply all changes
        transaction.Commit();
    }
    catch (Exception ex)
    {
        // Rollback in case of any error
        transaction.Rollback();
        throw;
    }
}
```

In this example, both the update and insert are performed within a single transaction. If any step fails, the rollback ensures that neither change is applied. Additionally, any related collections (for instance, if the user has associated orders or contacts) are automatically refreshed to reflect the new state.

---

## 4. Summary

DataLinq’s approach to caching and mutation is designed to:
- **Optimize Queries:**  
  By initially fetching only primary keys and then bulk-loading missing rows, it minimizes overhead.
- **Ensure Consistency:**  
  Immutable objects, combined with transactional caches, guarantee that data remains consistent even during concurrent operations.
- **Automate Relation Updates:**  
  Relation caches maintain indices of foreign key mappings, ensuring that related collections are always current—even when data is mutated within a transaction.
- **Provide a Robust Mutation API:**  
  With clear methods for updating, inserting, committing, and rolling back changes, DataLinq makes it straightforward to work with data while keeping performance and integrity front and center.

These strategies empower developers to build high-performance, scalable applications with confidence.