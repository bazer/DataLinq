# Your First Query and Update

At this point you should already have:

- installed the provider package
- installed the CLI
- generated models from your schema

Now the point of DataLinq becomes visible: you work through the generated model surface, query immutable instances, and update through mutable wrappers.

## Create the Database Object

Use the runtime provider that matches your database:

```csharp
using DataLinq;
using MyApp.Models;

var db = new MySqlDatabase<AppDb>(connectionString);
// Or: var db = new SQLiteDatabase<AppDb>(connectionString);
```

`AppDb` here is the generated database model type from your configuration and model generation step.

## Run Your First Query

Query through the generated table properties:

```csharp
var activeUsers = db.Query().Users
    .Where(x => x.IsActive)
    .OrderBy(x => x.UserId)
    .ToList();
```

The important part is not the syntax alone. The important part is the model:

- `db.Query()` gives you the generated database surface
- `Users` is a generated strongly typed table entry
- the results are immutable model instances

## Update a Row

Read an immutable instance, mutate it, then save it:

```csharp
var user = db.Query().Users.Single(x => x.UserId == userId);

var updatedUser = user
    .Mutate(x => x.DisplayName = "Updated Name")
    .Save();
```

That is the core DataLinq write flow:

1. read immutable data
2. create a mutable wrapper
3. save through the mutation API
4. get back a fresh immutable instance

## Access a Relation

Generated relations are lazy and cache-aware:

```csharp
var department = db.Query().Departments.Single(x => x.DeptNo == "d005");
var managers = department.Managers;
```

That first relation access can trigger relation resolution and caching. It is not pretending to be a plain in-memory property.

## Where to Go Next

Once this basic path makes sense, use these pages next:

- [Querying](../Querying.md)
- [Caching and Mutation](../Caching%20and%20Mutation.md)
- [Transactions](../Transactions.md)
- [Supported LINQ Queries](../Supported%20LINQ%20Queries.md)
