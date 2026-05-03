# Support Matrices

This is the central index for DataLinq's support and test matrices.

The rule is simple: if a matrix describes current behavior, it belongs here. Roadmap folders may link to these pages, but they should not own the canonical copy.

## Matrix Inventory

| Matrix | What it answers | Canonical evidence | Update when |
| --- | --- | --- | --- |
| [LINQ Translation Support Matrix](LINQ%20Translation%20Support%20Matrix.md) | Which LINQ translation shapes are covered by active tests, and which shapes remain unsupported or unproven. | Compliance tests under `src/DataLinq.Tests.Compliance/Query` and `src/DataLinq.Tests.Compliance/Translation`. | A LINQ translator feature, diagnostic, or support boundary changes. |
| [Provider Metadata Support Matrix](Provider%20Metadata%20Support%20Matrix.md) | Which MySQL, MariaDB, and SQLite metadata features DataLinq preserves through the supported read/generate/read roundtrip. | Provider metadata roundtrip tests and provider metadata factories. | Metadata import, generated SQL, schema validation, or provider DDL support changes. |
| [Test Provider Matrix](Test%20Provider%20Matrix.md) | Which local and server-backed targets the test infrastructure knows how to run. | `test-infra/podman/matrix.json` plus the testing matrix catalog. | A server target, target alias, profile, image, or port changes. |

## Public Contract vs Maintenance Evidence

The shorter public contract pages are still the best starting point for normal users:

- [Supported LINQ Queries](../Supported%20LINQ%20Queries.md)
- [Querying](../Querying.md)
- [MySQL & MariaDB](../backends/MySQL-MariaDB.md)
- [SQLite](../backends/SQLite.md)

The matrices are intentionally more detailed. They are where maintainers check whether a claim is backed by tests, which is the only standard that matters here.

## Update Rules

- Update the relevant matrix in the same change that changes the supported surface.
- Keep support claims tied to tests, generated artifacts, or an executable configuration file.
- Prefer linking to these pages from roadmap material instead of creating another matrix copy.
- Do not promote a feature from partial or unsupported to supported unless the active test suite proves it.
