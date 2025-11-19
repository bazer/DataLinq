### **Architectural Vision: Application Patterns for DataLinq**

#### **1. Introduction & Guiding Philosophy**

DataLinq's core strength lies in its opinionated design: immutability, high-performance reads via an aggressive cache, and explicit, transactional writes. To date, the focus has been on the core ORM mechanics. This document outlines a vision for how to best integrate DataLinq into modern application architectures, ensuring that the patterns we promote are a natural extension of its core philosophy.

Our guiding principles for this architectural evolution are:
*   **Embrace Asymmetry:** Acknowledge and leverage the fundamental difference between our highly optimized read path and our safe, transactional write path.
*   **Promote Clarity:** The developer's intent—whether reading or changing data—should be obvious from the code they write and the dependencies they inject.
*   **Enforce Correctness:** The design should guide developers toward safe and correct usage, particularly concerning transaction management, rather than relying on convention.
*   **Minimize Ceremony:** Avoid boilerplate and unnecessary layers of abstraction that do not add significant value, staying true to DataLinq's lightweight ethos.

---

#### **2. Core Pattern: In-Process CQRS (Command Query Responsibility Segregation)**

The most natural architectural pattern for DataLinq is a simple, in-process CQRS. This pattern formally separates the responsibility of changing state (Commands) from reading state (Queries).

*   **The Command Stack (for Writes):** This path will be responsible for all data mutations. It will be explicitly transactional, ensuring atomicity and consistency. The primary tool for this stack is the **Unit of Work**.
*   **The Query Stack (for Reads):** This path will be responsible for all data retrieval. It will be non-transactional, read-only, and designed to leverage DataLinq's caching mechanisms for maximum performance.

This separation is not about distributed systems or message queues; it's a logical separation within a single application that makes the entire system easier to reason about, optimize, and maintain.

---

#### **3. The Unit of Work & Transaction Management**

The concept of a Unit of Work (UoW) is critical for managing writes. It ensures that a business operation, which may involve multiple changes, is treated as a single atomic unit.

##### **3.1. The UoW Implementation: The `IDataLinqSession`**
Instead of a generic `IUnitOfWork` interface, we will formalize the `Transaction<T>` object as the UoW. To ensure correctness in nested operations, we will introduce two distinct interfaces:

*   `IDataLinqSession<TDatabase>`: Represents a **participant** in a transaction. It provides methods for querying and mutating data (`Insert`, `Update`, `Delete`, `Save`) and for vetoing the transaction (`Rollback`). It **will not** have a `Commit()` method. This is the interface that the vast majority of business services will consume.
*   `IUnitOfWork<TDatabase>`: Represents the **coordinator** of a transaction. It inherits from `IDataLinqSession` and adds the exclusive right to `Commit()` the transaction. This interface is intended for use only by the top-level orchestrator of a business operation (e.g., an application's middleware).

##### **3.2. Lifecycle Management: The `IDataLinqSessionFactory`**
To manage the creation and sharing of the ambient transaction within a logical operation, we will introduce a **Session Factory**.

*   **Purpose:** The factory's primary role is to provide the single, shared transaction for a given scope (e.g., an HTTP request or a background job).
*   **Mechanism:** It will use `AsyncLocal<T>` to maintain the ambient `IUnitOfWork`. The first call to `BeginUnitOfWork()` in a logical flow will create the transaction; subsequent nested calls within the same flow will receive a reference to the same transaction.
*   **Boundary Control:** The transaction's boundary and lifetime will be made explicit through a `using` block (`using (var uow = factory.BeginUnitOfWork()) { ... }`), ensuring that `Commit` or `Rollback` is always handled correctly by the root operation.

---

#### **4. Domain-Driven Design (DDD) & The Repository Pattern**

DataLinq will encourage the principles of DDD while rejecting some of its more dogmatic and often counterproductive tactical patterns.

##### **4.1. Rich Domain Models via `partial class`**
DataLinq's source generation of `abstract partial` classes is the perfect mechanism for creating Rich Domain Models.
*   **DataLinq's Responsibility:** The source generator will continue to own the data and persistence concerns of the model (properties mapping to columns, relations, etc.).
*   **Developer's Responsibility:** The developer will own a separate `partial class` file where they can add business logic, validation rules, and methods that encapsulate domain behavior (e.g., `employee.GiveRaise()`).

This provides a clean and powerful separation of concerns.

##### **4.2. The Repository Pattern: A Deliberate Rejection of Generics**
We will actively steer developers *away* from the generic repository pattern (`IRepository<T>`). It is a leaky abstraction that adds little value on top of a modern ORM.

Instead, we will promote two distinct patterns for data access:

*   **The Write Path (Commands):** Services that handle commands will inject the `IDataLinqSession<TDatabase>`. The session's `Query()` method provides direct, repository-like access to the database tables for the purpose of fetching aggregates that need to be modified.
*   **The Read Path (Queries):** For reads, we promote two options:
    1.  **Direct Access:** For simple queries, services can inject `ReadOnlyAccess<TDatabase>` and use its `Query()` method.
    2.  **Dedicated Query Services:** For complex, reusable business queries, developers should create dedicated, stateless "Query Services" (e.g., `EmployeeQueries`). These services encapsulate the specific LINQ queries needed by the application and can be optimized to return DTOs or ViewModels directly.

---

#### **5. Dependency Injection (DI) Integration**

A first-class Dependency Injection story is non-negotiable. We will provide a simple, fluent extension method to register all necessary DataLinq components.

##### **5.1. The `AddDataLinq<TDatabase>()` Extension**
This single extension method will handle the registration of all core services with the correct lifetimes:

*   **`Database<TDatabase>`:** Registered as a **Singleton**. It holds the global cache and is expensive to initialize.
*   **`IDataLinqSessionFactory<TDatabase>`:** Registered as a **Singleton**. The factory itself is stateless.
*   **`IDataLinqSession<TDatabase>`:** Registered as **Scoped**. A new instance is resolved for each scope (e.g., web request), which retrieves the ambient transaction from the factory.
*   **`ReadOnlyAccess<TDatabase>`:** Registered as **Scoped**. Provides a lightweight, non-transactional context for reads within the same scope.

This DI strategy makes the entire architecture clean, testable, and easy to consume in any modern .NET application.