# Benchmark Results

This page shows the small stable benchmark subset that DataLinq publishes from CI.

The point is not to pretend these numbers are universal truth. They are not.

They are useful because they are:

- run repeatedly on one controlled CI lane
- limited to the current stable benchmark category, with planned macro categories only promoted after they are boring enough to trust
- currently trended only on the `sqlite-memory` provider to avoid redundant file-vs-memory noise
- stored in a consistent machine-readable format
- plotted over time so regressions, profile differences, and trend changes are visible

## Read This Honestly

These graphs are decision support, not marketing material.

Important limits:

- the numbers come from GitHub-hosted CI runners, so absolute timings still include runner variance
- the current suite is intentionally narrow and should be read as hot-path trend data, not as a full product performance verdict
- `default` and `heavy` benchmark profiles are shown together for context, but same-profile comparisons are the primary verdict
- high-noise rows should be treated with suspicion even when the line moves in an exciting direction

## Published Trends

<div
  id="benchmark-results-root"
  data-history-url="https://raw.githubusercontent.com/bazer/DataLinq/benchmark-data/benchmarks/history.json"
  data-latest-url="https://raw.githubusercontent.com/bazer/DataLinq/benchmark-data/benchmarks/latest.json"
  data-comparison-url="https://raw.githubusercontent.com/bazer/DataLinq/benchmark-data/benchmarks/latest-comparison.json"
  data-provider-filter="sqlite-memory">
  Loading benchmark history...
</div>

<script type="module" src="../public/benchmark-results.js"></script>
