> [!WARNING]
> This document is roadmap or specification material. It describes planned behavior rather than current DataLinq behavior.

# Specification: Relation-Aware Mutation API

**Status:** Draft / Approved Direction
**Goal:** Make related-entity mutation ergonomic without turning DataLinq into an implicit graph-diffing ORM. Users should still tell DataLinq exactly what to insert, update, delete, unlink, or relate. DataLinq should take care of foreign-key assignment, generated-key hydration, save ordering, transaction handling, and cache consistency.

## 1. Design Position

Relation-aware mutation should use the same `Insert`, `Update`, `Save`, and `Delete` entry points as ordinary row mutation.

There should not be a normal-user `SaveGraph()` path.

That split sounds precise, but it creates a worse API question: "Do I call `Save()` or `SaveGraph()`?" If the user picks the wrong one, related pending work may be ignored. That is a bad failure mode.

The better rule is:

> `Save()` saves the explicit mutation state contained in the mutable object. If that state includes pending relation operations, `Save()` executes a graph-aware save. If it does not, `Save()` executes the current row-scoped path.

The important constraint is that relation work must be explicit. DataLinq must not infer persistence operations by comparing loaded relation collections with current in-memory collection contents.

No silent collection diffing. No magic delete because something is missing from a list. No hidden insert because an object appears in a relation collection. Those are precisely the behaviors that make ORMs dangerous.

## 2. Relationship to Mutable Lifecycle

This plan depends on [Mutable Instance Lifecycle](Mutable%20Instance%20Lifecycle.md).

Graph-aware mutation must obey the same baseline and transaction rules:

- all graph operations run inside one transaction
- mutables touched by a committed graph save get committed baselines
- mutables touched by a rolled-back graph save become invalid for later writes
- a mutable bound to an active transaction cannot be reused through another transaction
- failed writes must not silently clear tracked changes

Relation-aware mutation adds dependency ordering and foreign-key assignment. It does not relax lifecycle correctness.

## 3. Core Principles

1. **Same save entry point.**
   `Save()` is graph-aware when pending relation operations exist.

2. **Explicit operations only.**
   Relation mutation is a command queue, not a collection diff.

3. **No ambiguous collection methods.**
   Do not expose `Add` or `Remove` as persistence operations. Use `Insert`, `Update`, `Save`, `Delete`, and `Unlink`.

4. **Foreign-key scalar assignment still works.**
   Existing code that manually assigns FK columns remains valid.

5. **Relation APIs assign keys, not intentions.**
   `child.Parent.Set(parent)` means "make this child point at that parent." If either side still needs insertion, graph save orders that work.

6. **Graph planning is mechanical.**
   DataLinq should not infer business intent. It should build a dependency graph from explicit row operations and relation bindings.

7. **Provider behavior must stay consistent.**
   SQLite, MySQL, and MariaDB should expose the same public semantics even if generated-key retrieval differs internally.

## 4. Terminology

### Relation binding

An explicit operation that says one mutable row should point at another row through relation metadata.

Example:

```csharp
salary.employees.Set(employee);
```

This replaces manual FK assignment:

```csharp
salary.emp_no = employee.emp_no.Value;
```

### Pending relation operation

An explicit operation queued on a mutable relation wrapper, such as `Insert`, `Save`, `Delete`, or `Unlink`.

### Mutation graph

The graph of row operations and dependencies discovered from the root mutable and its pending relation operations.

### Root mutable

The mutable passed directly to `Insert`, `Update`, `Save`, or `Delete`.

### Dependent mutable

A mutable reached through a pending relation operation.

### Reference relation

A generated relation from a dependent row to a single principal row, usually represented by FK columns on the current model.

Example:

```csharp
salary.employees.Set(employee);
```

### Collection relation

A generated relation from a principal row to many dependent rows.

Example:

```csharp
employee.salaries.Insert(newSalary);
```

## 5. Generated Mutable Relation Surface

Generated immutable models already expose relation properties. Generated mutable models should gain mutable relation wrappers for the same relation metadata.

Today immutable `Salaries` has:

```csharp
[Relation("employees", "emp_no", "salaries_ibfk_1")]
public abstract Employee employees { get; }
```

The generated mutable `MutableSalaries` should expose a reference relation wrapper:

```csharp
public MutableReferenceRelation<MutableSalaries, Employee> employees { get; }
```

Today immutable `Employee` has:

```csharp
[Relation("salaries", "emp_no", "salaries_ibfk_1")]
public abstract IImmutableRelation<Salaries> salaries { get; }
```

The generated mutable `MutableEmployee` should expose a collection relation wrapper:

```csharp
public MutableCollectionRelation<MutableEmployee, Salaries> salaries { get; }
```

The exact generic shape can change. The required behavior is what matters.

## 6. Reference Relation API

Reference relation wrappers handle many-to-one or one-to-one FK assignment from the current mutable to a related principal row.

Suggested surface:

```csharp
public sealed class MutableReferenceRelation<TOwner, TRelated, TMutableRelated>
{
    public void Set(TRelated related);
    public void Set(TMutableRelated related);
    public void Clear();
    public bool HasPendingBinding { get; }
}
```

Generated APIs should use concrete mutable types where possible. For the employees model, that means `salary.employees.Set(MutableEmployee employee)` rather than forcing callers through the base `Mutable<Employee>` type.

### 6.1. Set existing parent

```csharp
var employee = database.Query().Employees.Single(x => x.emp_no == empNo);

var salary = new MutableSalaries
{
    salary = 50000,
    FromDate = fromDate,
    ToDate = toDate
};

salary.employees.Set(employee);

var savedSalary = salary.Save(database);
```

Expected behavior:

1. `salary.employees.Set(employee)` copies `employee.emp_no` into `salary.emp_no`.
2. `salary.Save(database)` inserts salary.
3. No graph ordering is needed because the parent already has a committed key.

### 6.2. Set new parent

```csharp
var employee = new MutableEmployee(
    birthDate: birthDate,
    firstName: "Ada",
    gender: Employee.Employeegender.F,
    hireDate: hireDate,
    lastName: "Lovelace");

var salary = new MutableSalaries
{
    salary = 50000,
    FromDate = fromDate,
    ToDate = toDate
};

salary.employees.Set(employee);

var savedSalary = salary.Save(database);
```

Expected behavior:

1. `salary.employees.Set(employee)` records a dependency because `employee` is new.
2. `salary.Save(database)` opens one transaction.
3. DataLinq inserts `employee` first.
4. DataLinq hydrates `employee.emp_no`.
5. DataLinq assigns `salary.emp_no`.
6. DataLinq inserts `salary`.
7. `Save()` returns the root immutable salary.
8. The employee mutable is reset to the inserted employee baseline.

### 6.3. Reparent existing child

```csharp
var newEmployee = database.Query().Employees.Single(x => x.emp_no == newEmpNo);
var salary = database.Query().salaries.Single(x => x.emp_no == oldEmpNo && x.FromDate == fromDate).Mutate();

salary.employees.Set(newEmployee);

var savedSalary = salary.Save(database);
```

Expected behavior:

1. `Set(newEmployee)` records a relation binding and changes the FK columns.
2. `Save()` updates the salary row.
3. Relation/index cache invalidation uses old and new relation keys.

Primary key caveat:

If the FK columns are also part of the child's primary key, this is a primary-key mutation. Ordinary row update should reject it unless DataLinq later implements a dedicated key-migration path. For the employees `salaries` table, `emp_no` is part of the salary primary key, so the ordinary reparent example above should throw under the mutable lifecycle plan.

### 6.4. Clear nullable reference

```csharp
var payment = existingPayment.Mutate();

payment.orders.Clear();

var savedPayment = payment.Save(database);
```

Expected behavior:

- allowed only when every FK column in the relation is nullable
- sets relation FK columns to null
- updates the row on `Save()`
- invalidates old relation buckets

If the relation is not nullable, `Clear()` throws:

```text
Relation 'orders' cannot be cleared because one or more foreign-key columns are non-nullable. Use Delete(...) or Set(...) another related row.
```

## 7. Collection Relation API

Collection relation wrappers handle one-to-many relation operations from a principal row to dependent rows.

Suggested surface:

```csharp
public sealed class MutableCollectionRelation<TOwner, TRelated, TMutableRelated>
{
    public TMutableRelated Insert(TMutableRelated related);
    public TMutableRelated Update(TMutableRelated related);
    public TMutableRelated Save(TMutableRelated related);
    public void Delete(TRelated related);
    public void Delete(TMutableRelated related);
    public void Unlink(TRelated related);
    public void Unlink(TMutableRelated related);
    public int PendingOperationCount { get; }
}
```

Concrete generated wrappers should preserve fluent typing:

```csharp
MutableSalaries salary = employeeMutation.salaries.Insert(new MutableSalaries { /* ... */ });
```

Do not expose persistence-bearing `Add` or `Remove`.

### 7.1. Insert child through existing parent

```csharp
var employee = database.Query().Employees.Single(x => x.emp_no == empNo);
var employeeMutation = employee.Mutate();

var salary = employeeMutation.salaries.Insert(new MutableSalaries
{
    salary = 50000,
    FromDate = fromDate,
    ToDate = toDate
});

var savedEmployee = employeeMutation.Save(database);
var savedSalary = salary.GetImmutableInstance();
```

Expected behavior:

1. `employeeMutation.salaries.Insert(...)` records a child insert.
2. DataLinq copies `employee.emp_no` into `salary.emp_no`.
3. `employeeMutation.Save(database)` sees pending relation operations.
4. Since the root employee has no scalar changes, no employee update is issued.
5. DataLinq inserts the salary.
6. `Save()` returns the root immutable employee.
7. The child mutable is hydrated and reset; callers can use `GetImmutableInstance()` if they need the saved child.

### 7.2. Insert new parent with children

```csharp
var employee = new MutableEmployee(
    birthDate: birthDate,
    firstName: "Grace",
    gender: Employee.Employeegender.F,
    hireDate: hireDate,
    lastName: "Hopper");

var salary = employee.salaries.Insert(new MutableSalaries
{
    salary = 75000,
    FromDate = fromDate,
    ToDate = toDate
});

var title = employee.titles.Insert(new MutableTitles
{
    title = "Engineer",
    FromDate = fromDate,
    ToDate = toDate
});

var savedEmployee = employee.Save(database);
```

Expected order:

1. insert employee
2. hydrate generated employee key
3. assign employee key into salary and title
4. insert salary
5. insert title
6. commit

### 7.3. Update existing child through parent collection

```csharp
var employee = database.Query().Employees.Single(x => x.emp_no == empNo);
var employeeMutation = employee.Mutate();

var salary = employee.salaries.Single(x => x.FromDate == fromDate).Mutate();
salary.salary = 80000;

employeeMutation.salaries.Update(salary);

employeeMutation.Save(database);
```

Expected behavior:

- the collection relation verifies or assigns the child's FK to the parent
- the salary update is queued as an explicit operation
- the root employee is not updated unless it has scalar changes
- `Save()` updates the salary

### 7.4. Save existing-or-new child through parent collection

```csharp
var employeeMutation = employee.Mutate();

var salary = maybeSalary?.MutateOrNew(
    empNo: employee.emp_no.Value,
    fromDate: fromDate)
    ?? new MutableSalaries
    {
        FromDate = fromDate,
        ToDate = toDate,
        salary = 50000
    };

salary.salary = 82500;

employeeMutation.salaries.Save(salary);
employeeMutation.Save(database);
```

Expected behavior:

- if `salary.IsNew()` then insert it after assigning the parent FK
- otherwise update it after verifying or assigning the parent FK

### 7.5. Delete child through parent collection

```csharp
var employeeMutation = employee.Mutate();
var salary = employee.salaries.Single(x => x.FromDate == fromDate);

employeeMutation.salaries.Delete(salary);

employeeMutation.Save(database);
```

Expected behavior:

- queues an explicit delete
- verifies the salary belongs to the relation if enough key information is available
- deletes the salary before deleting the parent if the parent is also queued for delete
- invalidates row and relation caches

### 7.6. Unlink child through parent collection

```csharp
var orderMutation = order.Mutate();
var payment = order.payments.Single(x => x.Id == paymentId);

orderMutation.payments.Unlink(payment);

orderMutation.Save(database);
```

Expected behavior:

- allowed only when the dependent FK is nullable
- sets the dependent FK columns to null
- does not delete the dependent row
- throws for non-nullable relations

### 7.7. Reject ambiguous collection mutation

These should not exist as persistence APIs:

```csharp
employeeMutation.salaries.Add(salary);
employeeMutation.salaries.Remove(salary);
employeeMutation.salaries.Clear();
```

If a mutable relation wrapper exposes ordinary enumerable-like members for inspection, they must not imply persistence.

Preferred names stay explicit:

```csharp
Insert
Update
Save
Delete
Unlink
```

## 8. Graph-Aware Row Entry Points

The existing row mutation entry points should become relation-aware.

### 8.1. `Save`

```csharp
employee.Save(database);
transaction.Save(employee);
```

Behavior:

- if no pending relation operations exist, use ordinary row `Save`
- if pending relation operations exist, plan and execute the explicit mutation graph
- root row follows existing `Save` semantics: insert if new, update if existing and changed, no-op if existing with no scalar changes

### 8.2. `Insert`

```csharp
employee.Insert(database);
transaction.Insert(employee);
```

Behavior:

- root must be new
- pending relation operations are included in the same mutation graph
- graph dependencies can insert related principals before the root if the root depends on them
- root insert still returns the root immutable row

### 8.3. `Update`

```csharp
employee.Update(database);
transaction.Update(employee);
```

Behavior:

- root must already exist
- pending relation operations are included
- root update remains a no-op if root scalar changes are empty
- relation operations still execute even when root scalar update is a no-op

### 8.4. `Delete`

```csharp
transaction.Delete(employeeMutation);
```

Behavior:

- root must already exist
- compatible pending relation deletes/unlinks can execute before root delete
- incompatible pending inserts or updates throw
- DataLinq must not invent child deletes just because a parent is deleted
- database FK constraints still enforce referential integrity unless the user explicitly queued dependent work

## 9. Fluent Lambda Surface

Generated `Mutate(...)` helpers should work naturally with mutable relation wrappers.

### 9.1. Add child in a mutation lambda

```csharp
var employeeMutation = employee.Mutate(x =>
{
    x.last_name = "Updated";
    x.salaries.Insert(new MutableSalaries
    {
        salary = 90000,
        FromDate = fromDate,
        ToDate = toDate
    });
});

employeeMutation.Save(database);
```

### 9.2. Save through database helper

```csharp
var savedEmployee = database.Save(employee.Mutate(x =>
{
    x.salaries.Insert(new MutableSalaries
    {
        salary = 90000,
        FromDate = fromDate,
        ToDate = toDate
    });
}));
```

### 9.3. Save through transaction callback

```csharp
database.Commit(transaction =>
{
    var employeeMutation = employee.Mutate(x =>
    {
        x.salaries.Insert(new MutableSalaries
        {
            salary = 90000,
            FromDate = fromDate,
            ToDate = toDate
        });
    });

    transaction.Save(employeeMutation);
});
```

## 10. Direct Relation Binding Helpers

The generated relation wrapper properties are the primary API. DataLinq can also offer generic helpers for dynamic or generic code.

### 10.1. Reference helper

```csharp
salary.SetRelation(x => x.employees, employee);
```

Equivalent to:

```csharp
salary.employees.Set(employee);
```

### 10.2. Collection helper

```csharp
employeeMutation.Relation(x => x.salaries).Insert(salary);
```

Equivalent to:

```csharp
employeeMutation.salaries.Insert(salary);
```

These helpers are optional. They are useful when generated relation property names collide with local naming conventions or when code is operating over relation expressions.

## 11. Many-to-Many Relations

Version 1 should handle many-to-many through explicit join models only.

Example:

```csharp
var user = existingUser.Mutate();

var userRole = user.userroles.Insert(new MutableUserRole());
userRole.roles.Set(role);

user.Save(database);
```

DataLinq should not synthesize hidden join rows behind an API like:

```csharp
user.roles.Insert(role);
```

That higher-level convenience can come later if relation metadata explicitly models skip navigations. The first version should keep the join table visible because it is the row being inserted or deleted.

## 12. Composite Keys

Relation binding must support composite FK/candidate-key relations.

Example:

```csharp
var order = new MutableOrder
{
    TenantId = tenantId,
    OrderNumber = orderNumber
};

var line = order.lines.Insert(new MutableOrderLine
{
    LineNumber = 1,
    Quantity = 2
});

order.Save(database);
```

Expected behavior:

- if `TenantId` and `OrderNumber` are already known, both are copied into the line
- if any component is generated by the database, dependent assignment waits until the principal is inserted and hydrated
- if the principal still does not have a complete key after insert, graph save throws

## 13. Nested Multi-Hop Inserts

Nested relation operations can form a multi-hop graph as long as dependencies are acyclic.

```csharp
var order = new MutableOrder
{
    OrderedAt = now
};

var line = order.orderdetails.Insert(new MutableOrderdetail
{
    Quantity = 2
});

line.products.Set(product);

var payment = order.payments.Insert(new MutablePayment
{
    Amount = 100m
});

order.Save(database);
```

Expected order:

1. insert or validate `product` if it is a pending mutable
2. insert `order`
3. assign order FK into `line` and `payment`
4. assign product FK into `line`
5. insert `line`
6. insert `payment`

If all required keys are already committed, DataLinq can skip dependency inserts and just assign FK values.

## 14. Conflict Rules

### 14.1. Duplicate operations

The same mutable row should appear as a row operation at most once per mutation graph.

Allowed:

- several children reference the same pending parent
- the same parent is used as a dependency by multiple rows

Rejected:

- same mutable queued for both `Insert` and `Delete`
- same mutable queued for both `Unlink` and `Delete`
- same new mutable queued under two different parent collection relations for the same FK

### 14.2. Relation binding versus scalar FK assignment

Foreign-key scalar assignment remains supported, but relation binding owns the FK columns it binds.

Recommended rule:

- direct FK scalar assignments before `relation.Set(...)` are overwritten by the explicit relation binding
- direct FK scalar assignments after `relation.Set(...)` that conflict with the relation binding throw before SQL execution
- direct FK scalar assignments that agree with the relation binding are harmless

This keeps relation binding useful while catching contradictory user intent.

### 14.3. Required relation missing

If a new row has a non-nullable FK and neither scalar FK values nor a relation binding provide complete values, insert throws before SQL execution when possible.

Suggested exception:

```text
Cannot insert 'Salaries' because required relation 'employees' has no value and foreign-key column 'emp_no' is not set.
```

### 14.4. Cycles

Version 1 should reject cycles that require generated keys before either side can be inserted.

Allowed:

- cycles where all FK keys are already known and rows are only updated
- acyclic insert graphs

Rejected:

- new row A needs generated key from new row B while B needs generated key from A
- two-phase nullable FK insertion unless explicitly designed later

Suggested exception:

```text
The mutation graph contains a cycle between 'A' and 'B' that cannot be inserted without a two-phase foreign-key update. This is not supported.
```

## 15. Execution Model

Graph-aware save should build a plan from explicit row and relation operations.

### 15.1. Plan nodes

Each row operation becomes a node:

- insert row
- update row
- delete row
- unlink row by setting FK columns to null
- no-op root row with pending dependent operations

### 15.2. Plan edges

Dependency edges come from relation bindings:

- principal insert before dependent insert when dependent needs the principal key
- principal insert before dependent update when dependent FK changes to point at a new principal
- dependent delete or unlink before principal delete when both are explicitly queued and FK constraints require it

### 15.3. Hydration

Every inserted or updated row should be hydrated through the same mutation path used by ordinary row save.

Requirements:

- generated primary keys are available before dependent inserts
- default values and trigger-updated values are visible in returned immutable rows
- mutable baselines are reset only after confirmed hydration

### 15.4. Transaction boundary

Implicit graph save opens one transaction.

Explicit graph save uses the caller's transaction.

Graph save must not partially commit. Either all explicit graph operations succeed, or none do.

## 16. Return Values

Existing row mutation return values should stay source-compatible:

- `Save(rootMutable)` returns the root immutable row
- `Insert(rootMutable)` returns the root immutable row
- `Update(rootMutable)` returns the root immutable row

Related mutables are hydrated and reset in place.

Example:

```csharp
var employee = new MutableEmployee(...);
var salary = employee.salaries.Insert(new MutableSalaries { salary = 75000, FromDate = from, ToDate = to });

var savedEmployee = employee.Save(database);
var savedSalary = salary.GetImmutableInstance();
```

If a richer result object is added later, it should be additive and should not replace the normal `Save()` path.

## 17. Public Documentation Requirements

When implemented, this must be clearly specified in public docs.

At minimum, update:

- `docs/Caching and Mutation.md`
- `docs/Transactions.md`
- `docs/getting-started/Your First Query and Update.md`
- `docs/Troubleshooting.md`
- generated XML docs for mutable relation wrappers

The public docs should state:

- `Save()` is graph-aware only for explicit pending relation operations
- DataLinq never deletes, unlinks, inserts, or updates related rows by diffing loaded collections
- users must call `Insert`, `Update`, `Save`, `Delete`, or `Unlink` explicitly
- relation wrappers assign FK values and can defer generated-key assignment until save order is known
- graph-aware saves run in one transaction
- relation operations obey the mutable lifecycle and rollback invalidation rules
- direct FK scalar assignment remains supported
- many-to-many v1 uses explicit join models

## 18. Testing Requirements

Add compliance tests for SQLite, MySQL, and MariaDB covering:

- child reference `Set(existingParent)` assigns FK and inserts child
- child reference `Set(newParent)` inserts parent first, hydrates key, then inserts child
- parent collection `Insert(newChild)` inserts child with parent FK
- new parent with multiple child inserts saves in the correct order
- root with no scalar changes but pending child insert still executes child insert
- existing child update through parent collection updates only the child when root has no changes
- `Save(child)` through parent collection chooses insert versus update by child `IsNew()`
- delete through parent collection deletes child and invalidates relation caches
- unlink through nullable FK sets FK null and does not delete child
- unlink through non-nullable FK throws
- ordinary scalar FK assignment still works
- relation binding plus conflicting later scalar FK assignment throws
- relation binding after earlier scalar FK assignment sets relation FK values
- composite-key relation binding assigns all FK components
- generated-key parent insert hydrates before dependent insert
- many-to-many join row insertion uses explicit join model
- duplicate conflicting graph operations throw before SQL execution
- unsupported generated-key cycles throw before SQL execution
- rollback invalidates all touched mutables
- relation cache state is correct inside explicit transaction and after commit
- no loaded collection diffing occurs when user merely enumerates or omits relation rows

## 19. Implementation Checklist

- [ ] Add runtime mutable reference relation wrapper.
- [ ] Add runtime mutable collection relation wrapper.
- [ ] Generate mutable relation wrapper properties from relation metadata.
- [ ] Preserve existing scalar FK properties.
- [ ] Record pending relation operations on mutable instances.
- [ ] Add graph operation discovery from root mutable.
- [ ] Add graph dependency planner and topological sort.
- [ ] Add FK assignment and deferred generated-key resolution.
- [ ] Make `Save`, `Insert`, `Update`, and compatible `Delete` graph-aware.
- [ ] Hydrate all touched mutables through existing mutation reset paths.
- [ ] Integrate graph execution with mutable lifecycle provenance.
- [ ] Reject ambiguous or conflicting graph operations before SQL execution.
- [ ] Add provider compliance tests.
- [ ] Update public docs after behavior lands.
