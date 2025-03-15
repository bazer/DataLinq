# Contributing to DataLinq

Thank you for your interest in contributing to DataLinq! We welcome improvements of all kinds—from bug fixes and documentation updates to entirely new features. This guide will walk you through the contribution process and provide best practices for working with the DataLinq codebase.

---

## 1. Getting Started

### 1.1 Fork and Clone

1. **Fork the Repository:**  
   Click the **Fork** button on the project’s GitHub page to create a personal copy of the repository.

2. **Clone Your Fork Locally:**  
   ```bash
   git clone https://github.com/YourUsername/DataLinq.git
   cd DataLinq
   ```

3. **Add Upstream Remote (Optional but Recommended):**  
   ```bash
   git remote add upstream https://github.com/DataLinqOrg/DataLinq.git
   ```
   This helps you keep your fork in sync with the official repository.

### 1.2 Setting Up Your Environment

1. **Install .NET SDK:**  
   DataLinq targets .NET 6 or higher. Make sure you have the corresponding .NET SDK installed.

2. **Restore NuGet Packages:**  
   From the root directory, run:
   ```bash
   dotnet restore
   ```

3. **Build the Solution:**  
   ```bash
   dotnet build
   ```
   Ensure the solution builds without errors before proceeding.

### 1.3 Exploring the Codebase

- **src**  
  Contains the main DataLinq libraries (e.g., `DataLinq.Core`, `DataLinq.MySql`, `DataLinq.SQLite`, etc.).
- **docs**  
  Holds the project’s documentation, including this contributing guide.
- **tests**  
  (If present) Contains unit and integration tests. Some test projects may also live under `src/DataLinq.Tests` or similar directories.

---

## 2. Coding Guidelines

1. **Style and Conventions:**  
   - Use **.NET naming conventions** (PascalCase for classes and methods, camelCase for private fields, etc.).
   - Keep lines reasonably short (e.g., under 120 characters).
   - Avoid overly long methods; aim for clear, maintainable functions.

2. **Comments and Documentation:**  
   - Document complex logic or non-obvious decisions using `///` XML doc comments or inline comments.
   - If you’re adding a new public API, consider adding or updating doc comments to explain usage.

3. **Commit Messages:**  
   - Use short, descriptive commit messages.
   - Include references to issues or pull requests when applicable (e.g., “Fix #123: Add caching for user profiles”).

---

## 3. Testing

### 3.1 Running Tests

DataLinq includes unit and integration tests to ensure reliability and prevent regressions:

```bash
dotnet test
```

- **Unit Tests:** Focus on isolated components (e.g., caching, query parsing).
- **Integration Tests:** Validate end-to-end scenarios, often requiring a running database (e.g., MySQL or SQLite).

### 3.2 Adding New Tests

Whenever you fix a bug or add a feature:
1. **Write or Update Tests:** Confirm that your changes work as intended and do not break existing functionality.
2. **Test Locally:** Ensure all tests pass before pushing your changes.

---

## 4. Submitting a Pull Request

1. **Create a Feature Branch:**  
   ```bash
   git checkout -b feature/my-new-feature
   ```
2. **Make Your Changes:**  
   Commit early and often. Keep commits atomic and focused on a single topic.
3. **Push to Your Fork:**  
   ```bash
   git push origin feature/my-new-feature
   ```
4. **Open a Pull Request:**  
   On GitHub, open a PR from your feature branch to the `main` (or relevant) branch in the official DataLinq repository.
5. **Review Process:**  
   - A maintainer or community member may review your changes.
   - Be prepared to address comments or requested revisions.
   - Once approved, the PR is merged into the main repository.

---

## 5. Communication

1. **Issues:**  
   - Use GitHub Issues for bug reports, feature requests, and discussion.
   - Provide clear steps to reproduce bugs or rationale for new features.
2. **Discussions / Forum (If Available):**  
   If the project maintains a separate discussion board, consider posting general or open-ended questions there.

---

## 6. Code of Conduct

We strive to maintain a friendly, respectful community. By participating in this project, you agree to uphold a [Code of Conduct](https://example.com/code-of-conduct) that fosters a welcoming environment for all contributors.

---

## 7. License

DataLinq is open source software, released under the [MIT License](../LICENSE.md). By contributing, you agree that your contributions will be licensed under the same license.

---

## 8. Thank You!

Your contributions make DataLinq a better tool for everyone. Whether you’re fixing a typo, adding a new feature, or improving test coverage, we appreciate your effort. Feel free to reach out if you have any questions about contributing.

---

**Happy coding!**