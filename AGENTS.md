# Repository Instructions

- The user prefers to handle commits personally for now. Do not create commits, amend commits, or push changes unless the user explicitly asks for that in the current conversation.
- When finishing a coherent slice or feature and reaching a real stopping point, proactively include a suggested commit message at the end of the final response even if the user did not ask for one yet. Prefer the descriptive multi-line style the user has been approving: concise subject, blank line, then a body that explains the actual change and why it matters.
- Expanded agent workflow guidance lives in `docs/contributing/AI Assistant Guidance.md` and the internal tooling pages under `docs/contributing/`.
- The legacy xUnit projects have been removed. All new tests should go into the TUnit test projects such as `src/DataLinq.Tests.Unit`, `src/DataLinq.Tests.Compliance`, `src/DataLinq.Tests.MySql`, or other active TUnit-based test projects.
- For broad documentation or cleanup work, prefer starting with an audit and action plan before making large edits.
- Once the plan is agreed, prefer autonomous execution to a solid stopping point instead of repeated check-ins.
- Sub-agents are allowed for parallelizable repo work once the scope is clear.
- Documentation should be grounded in the current code and tests, and should not present roadmap material as shipped behavior.
- Prefer embedding diagrams into the docs where they belong rather than keeping standalone presentation-style diagram pages.
- For repo-wide documentation changes, prefer a final verification pass and run a docs build when feasible.
- For DocFX in this repo, prefer a real root `index.md` as the website homepage. Do not rely on `README.md` for the site landing page, because DocFX may treat it as the default root target if `index.md` is missing.
- Keep the docs entry pages separate by purpose: `README.md` for GitHub, root `index.md` for the website homepage, and `docs/index.md` for the documentation intro.
- For documentation structure, prefer an onboarding-first flow: `Intro`, then `Getting Started`, then `Usage`, followed by providers, internals, and deeper reference material.
- When changing docs navigation or site presentation, verify the generated `_site` output, not just the source markdown.
- When previewing DocFX locally, do not open built pages via `file://`; use a local HTTP server or `docfx serve`, because browser security will block module scripts.
- For NuGet release work, prefer the local `publish-nuget.ps1` workflow over ad hoc `dotnet pack`/portal upload steps.
- The user prefers to run NuGet publishes personally. Do not publish packages or automate releases unless explicitly asked in the current conversation.
- For local release tooling, prefer prompting for secrets at execution time over storing long-lived NuGet API keys in environment variables or config when manual execution is sufficient.
