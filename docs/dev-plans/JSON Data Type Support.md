# Specification: JSON Data Type Support

**Status:** Draft
**Goal:** Treat JSON columns not just as "strings," but as structured, queryable data types within the DataLinq ecosystem. This includes typed mapping to C# POCOs, deep LINQ querying, and efficient serialization strategies.

---

## 1. The Data Model (Storage & Mapping)

We need to map a Database Column (JSON/TEXT) to a C# Complex Type.

### 1.1. The `[JsonColumn]` Attribute
We introduce an explicit attribute to distinguish standard strings/byte arrays from JSON payloads.

```csharp
public class UserConfig 
{ 
    public string Theme { get; set; }
    public bool NotificationsEnabled { get; set; }
}

// In User Model
[JsonColumn] // Signals DataLinq to handle serialization/querying logic
[Column("config")]
public abstract UserConfig Config { get; } 
```

### 1.2. Storage Strategy (Lazy Deserialization)
To maintain DataLinq's high-performance characteristics, **we must not deserialize JSON eagerly.**
*   **In `RowData` (Cache):** Store the **Raw String** (or byte array). This keeps the cache compact and avoids allocations for data that might not be read.
*   **In `Immutable<T>`:** The property getter handles the deserialization.
    *   *Optimization:* Use `System.Text.Json` with Source Generators (`JsonSerializerContext`) for maximum speed and AOT compatibility.

---

## 2. Querying (The Complexity)

The LINQ provider must distinguish between traversing a **Foreign Key Relation** and traversing a **JSON Path**.

### 2.1. Path Translation
**Scenario:** `db.Users.Where(x => x.Config.Theme == "Dark")`

1.  **Visitor:** The `QueryBuilder` encounters `x.Config`.
2.  **Check:** It checks metadata. `Config` is NOT a `RelationProperty`. It is a `ValueProperty` with `[JsonColumn]`.
3.  **Switch Mode:** The visitor enters "JSON Path Mode".
    *   It accumulates subsequent member accesses (`.Theme`) into a path string (`$.Theme`).
4.  **Generation:** It asks the Provider to generate the function.
    *   **MySQL:** `JSON_UNQUOTE(JSON_EXTRACT(config, '$.Theme')) = 'Dark'` (or `config->>'$.Theme'`).
    *   **SQLite:** `json_extract(config, '$.Theme') = 'Dark'`.

### 2.2. Supported Operations
We should aim to support:
*   **Deep Equality:** `Where(x => x.Data.Address.City == "Stockholm")`
*   **Array Contains:** `Where(x => x.Tags.Contains("Admin"))`
    *   *MySQL:* `JSON_CONTAINS(tags, '"Admin"')`
    *   *SQLite:* `EXISTS (SELECT 1 FROM json_each(tags) WHERE value = 'Admin')`

---

## 3. Mutation & Change Tracking

### 3.1. "Replace All" Strategy (Phase 1)
DataLinq does not track changes *inside* mutable objects.
*   **Behavior:** When mutating a JSON property, the user modifies the POCO.
*   **Save:** DataLinq reserializes the entire POCO back to a string.
*   **Comparison:** We compare the *Serialized String* against the *Original String* to determine `HasChanges()`.
    *   *Note:* This is inefficient for massive documents but safe and correct.

### 3.2. Partial Updates (Phase 2 - Future)
Support `JSON_SET` or `JSON_PATCH` operations. This requires a specialized `JsonProxy<T>` wrapper to track changes to specific properties, which is likely out of scope for the initial implementation.

---

## 4. Validation & Schema

### 4.1. Schema Validation
The `datalinq validate` tool should check:
*   **MySQL:** Column type is `JSON`.
*   **SQLite:** Column type is `TEXT` (or has a check constraint `IS_JSON(col)`).

### 4.2. AOT Serialization
To ensure the JSON feature doesn't break Native AOT:
*   The Source Generator should ideally identify types used in `[JsonColumn]`.
*   It should generate a `JsonSerializerContext` for these types automatically.

---

## 5. Implementation Steps

1.  [ ] **Core:** Add `[JsonColumn]` attribute.
2.  [ ] **Runtime:** Update `RowData` / `Immutable<T>` to handle lazy `System.Text.Json` deserialization.
3.  [ ] **Querying:** Update `QueryBuilder` to detect JSON property traversal.
4.  [ ] **Providers:** Implement `GetSqlForJsonPath(...)` in MySQL and SQLite providers.
5.  [ ] **Mutation:** Implement change detection (serialization-based equality).