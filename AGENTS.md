# Repository Instructions

- At a coherent stopping point, review and stage only the changes belonging to the task. Preserve unrelated working-tree and staged changes.
- When a commit is sensible, ask whether to commit and recommend either a direct commit to `master` or a branch and PR. Resolve the destination with the user before committing; do not assume the current branch is the intended destination.
- Do not commit until the user approves the scoped change. Approval to commit includes pushing to the agreed remote branch unless the user explicitly requests a local-only commit. Do not ask again for actions already authorized for that task. Amending commits, rewriting history, merging PRs, and publishing packages require their own explicit authorization.
- Commit-message conventions live in `docs/Contributing.md`. Do not routinely post suggested commit messages in chat; provide one when requested.
- The agent development loop lives in `docs/contributing/AI Assistant Guidance.md`. Read it before implementation, then consult only the tooling pages relevant to the task.
- While iterating, run very focused tests for the behavior under change. Run `quick` once at a coherent stopping point for code changes; consider `latest` locally when MySQL/MariaDB behavior is affected. Leave broader coverage to CI by default. Use documentation checks for documentation-only changes.
- New tests belong in the active TUnit projects, not the removed legacy xUnit projects.
- Prefer the repo-local Dev, Testing, and Benchmark CLIs when they fit the task. See `docs/contributing/Internal Tooling.md` for routing, platform setup, and troubleshooting.
- When a sandbox is available, try build, test, local CLI, and non-destructive git inspection commands there first. Request escalation only after a likely sandbox, network, cache, or permission failure and only when the execution environment supports it. Native Windows commands and known failure modes are documented in Internal Tooling.
- For broad documentation or cleanup work, start with an audit and action plan. Once the scope is agreed, execute autonomously to a coherent stopping point.
- Sub-agents are allowed for parallelizable repo work once the scope is clear.
- Documentation must describe current code and tests; keep roadmap material in `docs/dev-plans/` distinct from shipped behavior.
- Keep entry pages separate: `README.md` for GitHub, root `index.md` for the DocFX homepage, and `docs/index.md` for the documentation intro. Keep onboarding first and embed diagrams in the relevant docs.
- For broad documentation changes, build the docs when feasible. For navigation or presentation changes, verify generated `_site` output and use HTTP, not `file://`, for browser previews.
- The user runs NuGet publishes unless explicitly requested otherwise. Prefer `publish-nuget.ps1` and prompting for secrets at execution time; do not store long-lived API keys for a manual release workflow.
