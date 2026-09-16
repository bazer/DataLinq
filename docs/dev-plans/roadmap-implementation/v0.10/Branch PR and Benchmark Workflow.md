> [!WARNING]
> This is an accepted release workflow, not evidence that 0.10 runtime features have shipped.

# Branch, PR And Benchmark Workflow

**Accepted:** 2026-09-16. Records the user's approval of the branch/PR proposal and addition of a website development benchmark line.

## Branches And Review

- Keep `master` as the stable integration/public documentation line while 0.10 is developed.
- Create `v0.10` from current master, including the accepted plans and evidence tooling. The locked published API compatibility baseline remains 0.9.2; it is not the source branch starting point.
- Use short-lived `codex/0.10-...` branches and separate worktrees for independent implementation slices. Feature PRs target `v0.10` and name their wave, accepted decisions, tests and evidence.
- Keep the shared integration branch buildable. Prefer landing a dependency before its dependent PR; explicitly identify any necessary stacked PRs.
- Squash ordinary feature PRs with descriptive commit messages. Use merge commits between master and the shared release branch, including final release integration. Do not rebase or force-push the shared branch.
- Fix defects affecting current consumers on master, then merge the fixes forward into the development branch rather than independently recreating them.
- Require PRs and the existing Latest required gate on the development branch, and disallow force pushes/deletion. A sole-maintainer workflow must not require an unavailable second GitHub approver; human review remains part of the working process.

## CI, Publication And Release Evidence

- PR CI and post-merge checks cover the development branch.
- Default-branch scheduling explicitly runs the full matrix and benchmark workflow against the configured development branch; merely adding cron to a nondefault branch does not schedule it.
- Keep branch-specific full-matrix count baselines and benchmark comparison inputs separate. Development results must not overwrite stable badges or silently become the stable comparison baseline.
- Run focused/required checks per PR, broader provider evidence at appropriate integration checkpoints, and strict performance evidence on clean frozen commits. Website trend telemetry does not replace W0/W8/W9 release gates.
- Infrastructure and website setup must reach master so default-branch scheduling and the public site can use it. Implement that setup through a dedicated PR before creating/protecting the release branch.
- Public documentation deployment becomes an explicit maintainer-controlled action; benchmark data can refresh independently without publishing unfinished API documentation.
- At release integration, pass W8 on the development branch, review/merge into master, then run W9 against the exact final commit before tagging or publishing. Earlier source-branch evidence cannot be relabeled as evidence for a newly created merge commit. NuGet publication remains a separate maintainer action.

## Stable And Development Benchmark Lines

- Run the existing published benchmark subset for both master and the active development branch, initially `v0.10`, on .NET 10. Use the same scenarios/provider selection; retain profile and actual runtime/toolchain identities.
- Extend the website chart with a stable line and an explicitly labeled development line. Keep default/heavy profiles and different recorded runtimes separate; show missing data honestly rather than copying or inventing a point.
- Use one tracked channel configuration to identify the active development branch/label. Future releases change that selection, for example from `v0.10` to `v0.11`, without rewriting workflow or chart logic.
- Preserve every retained run's actual branch, commit, timestamp, runtime, profile and immutable raw run identity. Rollover changes which branch is active, not the identity or label of historical data. Keep completed development history accessible.
- Partition retention/thinning and comparisons by branch/profile/runtime so one series cannot evict or compare against another accidentally. Serialize publication to the shared benchmark-data branch.
- Graphs remain diagnostic trend data from GitHub-hosted runners, with noise and environment limitations visible. A moving line is not a release pass or a performance guarantee.

## Initial Setup Verification

Before declaring setup complete: verify workflow/configuration behavior, meaningful series separation and rollover cases, the generated DocFX page in a browser, remote branch protection, required CI, and actual published benchmark runs for both branches. Record any unavailable evidence or permission limit explicitly.

The next implementation activity is still W0 baseline capture and I/O mapping, followed by W1 internal async/cancellation contracts. This workflow adds no runtime scope.
