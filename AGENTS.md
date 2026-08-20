# Repository Agent Guidance

## Roles

- The human user is the Product Director and owns product direction, priorities, and approval.
- The external task prompt acts as orchestrator and defines the current bounded slice.
- Codex is the implementation agent. Do not invent roadmap items, broaden scope, or replace product decisions.

## Product Direction

- `Matasuri` is the product-facing name.
- `Machine.*` remains the current internal architecture naming.
- Build a Windows-first, local-first intelligence layer.
- Normal operation must not require cloud inference, paid APIs, internet access, remote Ollama, or automatic model downloads.
- Production inference must eventually be owned and bundled by the application; the current local Ollama integration is transitional.
- Deterministic Windows capabilities provide verified facts and actions.
- Application code owns policy, validation, and safety.
- A language model may interpret or explain verified data, but must never invent machine state or directly execute arbitrary commands.
- Matasuri is a persistent single-instance Windows presence: startup enters the ambient orb; user interactions only expand or collapse the dashboard; secondary activations redirect to the primary; Esc, normal close, and sustained focus loss return to ambient presence. Real shutdown is reserved for Windows lifecycle and controlled development/test paths.
- DEBUG builds may invoke that controlled shutdown only through the redirected `matasuri-dev://shutdown` activation; it is never a product command or UI affordance.
- Finding severity and global machine posture are distinct. Localized historical reliability issues remain visible but do not automatically elevate the whole machine; posture is deterministic and reflects current machine-wide significance.
- Startup uses the packaged `MatasuriStartup` task and must respect Windows and user control.

## Repository Layout

```text
src/Machine.Core     → platform-neutral contracts and models
src/Machine.Windows  → deterministic Windows capabilities
src/Machine.Ollama   → local Ollama HTTP integration
src/Machine.App      → WinUI composition and presentation
tests/Machine.Tests  → xUnit tests
```

Dependency direction:

```text
Machine.App → Core, Windows, Ollama
Machine.Windows → Core
Machine.Ollama → Core
```

Do not reverse these dependencies.

## Source Map

- `Machine.Core` groups platform-neutral contracts and deterministic policy by `Observability`, `History`, `Learning`, `Intelligence`, and `Runtime` domains.
- `Machine.Windows` keeps read-only Windows acquisition under `Observability`, native boundaries under `Interop`, and process-owned facilities under `Runtime`.
- `Machine.Ollama` separates local runtime ownership, explanation/context construction, and wire payloads.
- `Machine.App` keeps window composition in `MainWindow`, ambient presence under `Ambient`, shell behavior under `Shell`, lifecycle coordination under `Lifecycle`, and each complete page under `Features`.
- `Machine.Tests` mirrors the same production domains.

New code should live with its domain or feature rather than in project-wide `Models`, `Services`, or `Helpers` buckets.

## Engineering Rules

- Inspect current code, README, Git state, and nearby patterns before editing.
- Treat the repository as the source of truth; never assume a previous prompt succeeded.
- Implement one narrow objective at a time with small vertical slices and minimal diffs.
- Apply SOLID pragmatically; avoid abstraction cathedrals.
- Do not add packages, projects, frameworks, services, factories, or generic abstractions unless genuinely required.
- Do not perform unrelated refactors.
- Keep UI formatting in `Machine.App`, Windows APIs in `Machine.Windows`, Ollama wire details in `Machine.Ollama`, and Core platform-neutral.
- Preserve cancellation, disposal, responsiveness, and previous successful UI data where applicable.
- Use read-only access unless the task explicitly authorizes a controlled mutation.
- Never execute arbitrary commands supplied by a model.
- Never silently download or load a model unless explicitly required.
- Keep automated tests deterministic and offline.
- Avoid subagents for normal feature slices; use them only when explicitly requested or when independent parallel work clearly justifies the cost.
- Read only relevant files, and keep command output and final reporting concise.

## Required Validation

For implementation work, run:

```powershell
dotnet build Machine.sln --configuration Debug
dotnet test Machine.sln --configuration Debug
```

Success requires:

- zero build errors;
- zero warnings unless the task documents an unavoidable existing warning;
- all tests passing;
- focused live validation when UI or Windows integration changes;
- no unrelated or generated files committed.

## Git Delivery

Unless the task explicitly says otherwise:

1. Review `git status` and the complete diff.
2. Stage only intended repository changes.
3. Commit with a focused conventional commit message.
4. Push to `origin/main`.
5. Verify local `HEAD` equals `origin/main`.
6. Verify the working tree is clean.

Never amend, force-push, discard user work, or create a branch or pull request unless instructed.

## Stop Conditions

Stop and report the blocker instead of improvising when:

- the working tree is unexpectedly dirty;
- baseline build or tests fail;
- required behavior needs an unapproved package or architecture change;
- safety boundaries would be violated;
- unrelated files would be committed;
- push would require force.

## Final Response Format

Keep the final response concise and include:

```text
Status
Implemented
Validation
Git
Files changed
Blockers
Current implementation status
Next step
```

Derive current implementation status from the repository and README. Include the commit, passing-test count, runtime/model status when relevant, and working-tree state. Do not repeat long command logs or claim validation that was not performed.
