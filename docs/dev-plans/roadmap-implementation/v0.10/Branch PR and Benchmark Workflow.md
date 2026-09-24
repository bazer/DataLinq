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

## W2 Single-PR Exception

**Accepted by the user, 2026-09-24:** implement W2 on `codex/0.10-w2` in one draft PR, **Implement W2: native provider async execution**, targeting `v0.10`. This replaces the separate-slice PR policy for W2 only.

- Keep small, coherent implementation commits with their relevant tests. Review completed milestones as work proceeds and keep the PR description/checklist current, distinguishing implementation, verification and blocked acceptance.
- Preserve meaningful W2 commits with a merge commit at final integration instead of the ordinary feature-PR squash. Keep the branch history stable; incorporate necessary integration-branch changes without rewriting recorded evidence commits.
- Run focused checks during development, broader provider checks at milestones and complete W2 evidence against the final candidate. Earlier checkpoint results retain their actual source identities.
- Keep the PR in draft while implementation or acceptance gates remain open. Opening and updating this PR does not authorize merging it or publishing packages.
- The user authorizes continuing W2 provider development while the official SQLite 10.0.13 fix is pending. Corrected official dependency adoption, affected evidence reruns, W0-F1 closure and SQLite acceptance remain explicit gates. Dependency fixes still land on `master`, merge forward to `v0.10`, then enter the W2 branch.

The [W2 implementation plan](W2%20Native%20Provider%20Async%20Execution.md) owns the operation/provider checklist and milestone evidence. Public API verification remains W3; this workflow changes no release scope.

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

The [W0 capture and I/O map](W0%20Baseline%20Evidence.md) are recorded. The next activity is the focused W0-F1 provider-lifetime follow-up; W1 internal async/cancellation contracts remain gated on W0 exit. Any correction affecting current consumers follows the master-first policy above. This workflow adds no runtime scope.
