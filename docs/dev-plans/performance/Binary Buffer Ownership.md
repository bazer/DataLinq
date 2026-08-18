# Binary buffer ownership

This page records the ownership contract and benchmark receipt for issue #49. Binary payloads are
mutable, so an allocation is removable only when the receiving layer becomes the sole owner. An
array retained by two layers is not "effectively owned" merely because one layer promises to be nice.

## Ownership and lifetime

| Boundary | Owner after the boundary | Lifetime | Copy policy |
| --- | --- | --- | --- |
| Memory source value | Memory store/caller | Store or caller lifetime | Canonical decode clones; the source is not transferable. |
| SQLite physical value | `Microsoft.Data.Sqlite` row cache | Current reader row | Not transferable because `SqliteDataRecord` retains the array. |
| MySQL physical value | MySqlConnector packet/row | Current reader row | `SqlDataLinqDataReader` fills a new exact array and transfers it. |
| Canonical provider row | `CanonicalProviderValueRow` | Source-result/materialization lifetime | Takes the MySQL exact buffer; clones borrowed memory/SQLite buffers. |
| Model row | `RowData` | Immutable/cached-row lifetime | Always clones binary values crossing from the canonical owner. |
| Cache publication | Cached immutable row | Cache-entry lifetime | Retains the model row; it does not clone non-key payloads. |
| Public model access | Caller | Caller-controlled | Returns a detached clone; callers cannot mutate cached model state. |

`IDataLinqOwnedBinaryBufferReader` is an opt-in provider SPI. Returning from
`TakeOwnedBytes` transfers an exact, independently allocated array; the provider must not retain or
mutate it. Readers that do not implement the SPI keep the defensive canonical clone. MySQL implements
the capability. SQLite intentionally does not, because its ADO reader caches the blob array.

Empty arrays are values and remain distinct from database `NULL`. SQL readers return an exact empty
array for a non-null zero-length blob and `null` only for database `NULL`.

## Copy accounting

The table counts payload bytes copied for an identity-mapped binary cell. Array headers and row/control
objects appear in allocated B/op but are not payload bytes.

| Stage | Memory | SQLite | MySQL before | MySQL after |
| --- | ---: | ---: | ---: | ---: |
| Provider read | 0 retained-source bytes | `N` into provider cache | `N` into provider array | `N` directly into transferred exact array |
| Canonical ownership | `N` | `N` | `N` redundant clone | 0 |
| Model ownership | `N` | `N` | `N` | `N` |
| Cache publication (non-key payload) | 0 | 0 | 0 | 0 |
| Public detached access | `N` | `N` | `N` | `N` |

The previous pooled helper was not a win for retained values: it filled an `ArrayPool<byte>` scratch
array, copied scratch into a new exact array, returned scratch, and the canonical decoder could still
clone the exact array. The MySQL owned path now has one exact allocation and one ADO fill. SQLite's
public `GetBytes` path clones the ADO-owned cached array directly and no longer rents an intermediate
scratch array. The remaining MySQL span overload returns its scratch array in `finally` and never
publishes or retains that scratch reference.

## Benchmark receipt

Harness: `BinaryOwnershipBenchmarks`, .NET 10.0.11, BenchmarkDotNet ShortRun, in-process toolchain,
live `mysql-8.4` on loopback. The same harness was patched onto parent commit `e40a9419` for the
baseline. ShortRun latency intervals are broad, but the payload-sized allocation delta is exact and
repeats at every size.

| Payload | Before allocation | After allocation | Before mean | After mean |
| ---: | ---: | ---: | ---: | ---: |
| 32 B | 0.18 KiB (184 B) | 0.13 KiB (128 B) | 0.3709 us | 0.1359 us |
| 4 KiB | 8.12 KiB | 4.09 KiB | 1.5535 us | 0.5586 us |
| 64 KiB | 128.12 KiB | 64.09 KiB | 14.1662 us | 6.7906 us |

The removed allocation is exactly one payload-length array (payload plus its CLR array overhead).
The focused smoke matrix also exercises provider read, canonical decode, model materialization, cache
publication, and detached access at 32 B, 4 KiB, and 64 KiB for memory, SQLite, and live MySQL.

As a non-binary control, the existing canonical provider-row decoding benchmark was run with
BenchmarkDotNet MediumRun against the same parent and current revision. Allocation remained
0.29 KiB/op; mean latency was 0.9229 us before and 0.9136 us after (-1.0%). The current decoder tests
the opt-in reader capability before consulting binary provider metadata, preserving the ordinary
reader fast path.

The native BenchmarkDotNet toolchain cannot enumerate the sandboxed Windows INetCache path in this
environment. Set `DATALINQ_BENCHMARK_IN_PROCESS=true` to select the explicit in-process toolchain;
this changes process isolation, so receipts must use the same setting on both revisions.
