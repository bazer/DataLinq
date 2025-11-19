# Specification: Batched Mutations & Optimistic Concurrency

**Status:** Draft / Approved Concept
**Primary Goal:** drastically improve write performance by reducing database round-trips while introducing robust optimistic concurrency control without requiring database schema changes.

---

## 1. Core Philosophy & Constraints

1.  **"Drop-in" Compatibility:** The concurrency features must work on existing user databases without requiring migrations, schema changes, or stored procedures.
2.  **Network Efficiency:** Reduce the "Chatty I/O" problem. Multiple operations (`INSERT`, `UPDATE`, `DELETE`) within a transaction should be sent in as few network packets as possible.
3.  **Explicit Ordering:** We do not solve the "Topological Sort" problem. Commands are executed in the order the user calls them (FIFO).
    *   *Rule:* Parents must be added before Children.
4.  **Optimistic Concurrency:** Every `UPDATE` and `DELETE` must ensure the row has not changed since it was read.

---

## 2. The "Ghost Hash" Concurrency Strategy

To support concurrency checking without adding a `Version` or `Timestamp` column to the user's schema, we will calculate a hash of the row's state at the time of execution.

### 2.1. The Mechanism
1.  **Read:** When fetching a row, we calculate (or fetch) a hash of its values.
2.  **Write:** The `UPDATE` statement includes a `WHERE` clause comparing the Database's current calculated hash against the C# cached hash.

### 2.2. Provider Implementations
*   **MySQL / MariaDB:** Use native SQL functions.
    *   `MD5(CONCAT_WS('|', COALESCE(Col1, ''), COALESCE(Col2, '')...))`
*   **SQLite:** Use Application-Defined SQL Functions (UDF).
    *   Register a C# function `DL_HASH` on connection open via `SqliteConnection.CreateFunction`.
    *   SQL: `DL_HASH(COALESCE(Col1, '') || '|' || ...)`

### 2.3. Handling "Empty" Updates
*   If `mutable.HasChanges()` is false, the operation is **dropped** from the batch entirely. No SQL is generated.
*   This avoids unnecessary round trips and prevents "false positive" concurrency errors where the hash matches but no rows were physically changed.

---

## 3. Batch Execution Strategies

Due to engine differences, we will employ two distinct strategies for batch execution and verification.

### 3.1. Strategy A: The "Accumulator Variable" (MySQL / MariaDB)
*Problem:* MySQL does not consistently support `RETURNING` for `UPDATE` statements, making it hard to verify if a specific command in a batch succeeded or failed due to concurrency.

**The Logic:**
1.  **Connection String:** Must force `UseAffectedRows=false` (Client Found Rows). This ensures `ROW_COUNT()` returns 1 even if values didn't physically change, provided the Hash matched.
2.  **SQL Generation:**
    ```sql
    SET @acc = 0;

    -- 1. INSERT (Parent)
    INSERT INTO Users (Name) VALUES ('Alice');
    SET @acc = @acc + ROW_COUNT();
    SET @ref_user_id = LAST_INSERT_ID(); -- Capture ID for dependencies

    -- 2. INSERT (Child) using chained variable
    INSERT INTO Orders (UserId) VALUES (@ref_user_id);
    SET @acc = @acc + ROW_COUNT();

    -- 3. UPDATE with Ghost Hash
    UPDATE Products SET Stock=9 WHERE Id=100 
    AND MD5(...) = 'OldHash';
    SET @acc = @acc + ROW_COUNT();

    -- Final Verification
    SELECT @acc;
    ```
3.  **Verification:** C# compares the returned `@acc` integer against the number of commands sent. If they don't match, throw `ConcurrencyException` and Rollback.

### 3.2. Strategy B: The "RETURNING" Clause (SQLite)
*Problem:* SQLite does not support persistent session variables (`SET @var`) in batch scripts.
*Advantage:* SQLite supports `RETURNING` on all DML statements and has zero network latency.

**The Logic:**
1.  **SQL Generation:** Concatenate statements normally.
    ```sql
    INSERT INTO Users (Name) VALUES ('Alice') RETURNING Id;
    INSERT INTO Orders (UserId) VALUES (last_insert_rowid()) RETURNING Id;
    UPDATE Products SET Stock=9 WHERE Id=100 AND DL_HASH(...) = 'OldHash' RETURNING Id;
    ```
2.  **Execution:** Use `ExecuteReader`.
3.  **Verification:** Iterate through `reader.NextResult()`. Count the number of result sets that returned a row. If an `UPDATE` fails concurrency, it returns an empty result set.
4.  **Validation:** If `Count(SuccessfulResults) != CommandCount`, throw `ConcurrencyException`.

---

## 4. Hydration (Post-Write)

We cannot assume the state of the object after a write due to:
1.  Database Triggers modifying data.
2.  Default values (timestamps).
3.  Auto-Increment IDs.

**Requirement:** Every modified entity must be re-fetched (hydrated) immediately within the transaction.

*   **MySQL:** Append `SELECT * FROM Table WHERE Id = LAST_INSERT_ID()` (or specific ID for updates) after every operation in the batch.
*   **SQLite:** Use `RETURNING *` clause in the batch statements.

**Mapping:**
The `Transaction` class must maintain a `Queue<Mutable<T>>` corresponding to the batch order. As the DataReader iterates results, it pops the next Mutable entity from the queue and updates its internal `RowData` and `Immutable` snapshot.

---

## 5. Optional Optimization: Explicit Versioning

To allow power users to bypass the "Ghost Hash" CPU overhead, we will introduce a `[RowVersion]` attribute.

**Usage:**
```csharp
[RowVersion]
[Column("version_tick")]
public abstract long Version { get; }
```

**Behavior Change:**
If this attribute is present on a model:
1.  **Disable Ghost Hashing** for that table.
2.  **Logic:**
    *   `INSERT`: Value defaults to 1.
    *   `UPDATE`: `SET version_tick = version_tick + 1 WHERE Id = @Id AND version_tick = @OldVersion`.
3.  **Benefit:** Faster database execution (integer comparison vs string hashing).

---

## 6. Implementation Checklist

- [ ] **DataLinq.SharedCore:** Add `[RowVersion]` attribute.
- [ ] **DataLinq.SQLite:**
    - [ ] Implement `DL_HASH` UDF injection on connection open.
    - [ ] Implement `RETURNING` based batch generation.
- [ ] **DataLinq.MySql:**
    - [ ] Implement `Accumulator Variable` batch generation.
    - [ ] Implement Variable Chaining for Foreign Keys (`LAST_INSERT_ID()`).
    - [ ] Enforce `UseAffectedRows=false` in connection string builder.
- [ ] **Transaction Class:**
    - [ ] Refactor `Commit()` to group operations.
    - [ ] Implement "Drop Empty Updates" logic.
    - [ ] Implement result set hydration loop.
    - [ ] Implement "Total Count" validation logic.