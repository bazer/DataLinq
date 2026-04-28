> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.
# Roadmap Implementation

## Purpose

`docs/dev-plans/roadmap-implementation` is where DataLinq turns the high-level roadmap into concrete execution plans.

`Roadmap.md` answers:

What should happen next, and why?

This folder answers:

What are we actually going to do, in what order, and how will we know it worked?

## Structure

- `phase-1-benchmarking-and-observability/`
- `phase-2-metadata-generator-and-diagnostics-hardening/`
- `phase-3-query-and-runtime-hot-path-optimization/`
- `phase-4-product-trust-features/`

## Notes

- These documents should stay grounded in the current codebase and benchmark harness, not in idealized architecture sketches.
- If a phase plan disagrees with `Roadmap.md`, the roadmap should usually be updated rather than letting the two silently diverge.
- A phase plan should be implementation-oriented: workstreams, sequencing, risks, verification, and exit criteria.
