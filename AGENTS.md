# Repository Instructions

- The user prefers to handle commits personally for now. Do not create commits, amend commits, or push changes unless the user explicitly asks for that in the current conversation.
- For broad documentation or cleanup work, prefer starting with an audit and action plan before making large edits.
- Once the plan is agreed, prefer autonomous execution to a solid stopping point instead of repeated check-ins.
- Sub-agents are allowed for parallelizable repo work once the scope is clear.
- Documentation should be grounded in the current code and tests, and should not present roadmap material as shipped behavior.
- Prefer embedding diagrams into the docs where they belong rather than keeping standalone presentation-style diagram pages.
- For repo-wide documentation changes, prefer a final verification pass and run a docs build when feasible.
- For NuGet release work, prefer the local `publish-nuget.ps1` workflow over ad hoc `dotnet pack`/portal upload steps.
- The user prefers to run NuGet publishes personally. Do not publish packages or automate releases unless explicitly asked in the current conversation.
- For local release tooling, prefer prompting for secrets at execution time over storing long-lived NuGet API keys in environment variables or config when manual execution is sufficient.
