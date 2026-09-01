# Repository Agent Guidance

## Roles

- The human user is the Product Director and owns product direction, priorities, and approval.
- The external task prompt acts as orchestrator and defines the current bounded slice.
- Codex is the implementation agent. Do not invent roadmap items, broaden scope, or replace product decisions.

## Product Direction

- `Matasuri` is the product-facing name.
- `Machine.*` remains the current internal architecture naming.
- Build a Windows-first, local-first intelligence layer.
- Normal operation must not require cloud inference, paid APIs, internet access, an external model server, or automatic model downloads.
- Production inference is owned by the application through pinned, verified llama.cpp and Qwen artifacts; no separately installed inference runtime is required.
- Deterministic Windows capabilities provide verified facts and actions.
- Application code owns policy, validation, and safety.
- A language model may interpret or explain verified data, but must never invent machine state or directly execute arbitrary commands.
- Matasuri is a persistent single-instance Windows presence: startup enters the ambient orb; user interactions only expand or collapse the dashboard; secondary activations redirect to the primary; Esc, normal close, and sustained focus loss return to ambient presence. Real shutdown is reserved for Windows lifecycle and controlled development/test paths.
- DEBUG builds may invoke that controlled shutdown only through the redirected `matasuri-dev://shutdown` activation; it is never a product command or UI affordance.
- Finding severity and global machine posture are distinct. Localized historical reliability issues remain visible but do not automatically elevate the whole machine; posture is deterministic and reflects current machine-wide significance.
- Matasuri-owned runtime failures remain visible as self-health diagnostics and are excluded from generic third-party application-failure findings and global posture.
- Startup uses the packaged `MatasuriStartup` task and must respect Windows and user control.

## Repository Layout

```text
src/Machine.Core     → platform-neutral contracts and models
src/Machine.Windows  → deterministic Windows capabilities
src/Machine.Inference → app-owned local inference and grounded generation
src/Machine.App      → WinUI composition and presentation
tests/Machine.Tests  → xUnit tests
```

Dependency direction:

```text
Machine.App → Core, Windows, Inference
Machine.Windows → Core
Machine.Inference → Core
```

Do not reverse these dependencies.

## Source Map

- `Machine.Core` groups platform-neutral contracts and deterministic policy by `Actions`, `Observability`, `History`, `Learning`, `Intelligence`, and `Runtime` domains.
- `Machine.Windows` keeps read-only Windows acquisition under `Observability`, allowlisted deterministic mutations under `Actions`, native boundaries under `Interop`, and process-owned facilities under `Runtime`.
- `Machine.Inference` separates private runtime ownership, explanation/context construction, and local wire payloads.
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
- Keep UI formatting in `Machine.App`, Windows APIs in `Machine.Windows`, local inference/runtime wire details in `Machine.Inference`, and Core platform-neutral.
- Preserve cancellation, disposal, responsiveness, and previous successful UI data where applicable.
- Use read-only access unless the task explicitly authorizes a controlled mutation.
- Never execute arbitrary commands supplied by a model.
- Never silently download or load a model unless explicitly required.
- Keep automated tests deterministic and offline.
- Learning activity auditing is diagnostic-only: keep it separately bounded and persistence failures must never alter, repair, or block learning state.
- Hardware evidence must distinguish measured sensors, Windows-reported state, and software estimates; power and energy estimates never change Learning or posture.
- Today electricity cost is deterministic observed-PC cost derived from additive History energy and the matching published rate; it is never the household bill.
- `Running bill today` is persistent Today status, not an arbitrated Local Insight; Today changes never signal New Insight or trigger inference.
- Session energy resets on restart; Today History does not.
- Learning schema v4 preserves v3 behavioral evidence and adds context-specific software-estimated whole-PC wall-power behavior from v4 observations onward; never backfill it from History.
- Learning is inspectable from the first accepted sample. Evidence maturity limits authority, not visibility; never add observation-period or calibration-countdown gating.
- The whole-machine situation projection is deterministic and bounded. It selects normalized Now, Recently, Learned, Today, Forward, action-outcome, Learning-confidence, and separate self-health evidence without exposing raw persistence, network addresses, location, or recovery payloads.
- Global Learning health, current-context maturity, power maturity, and recurring-pattern readiness are separate facts. Pattern readiness must report the actual sample/day/adjacency blocker without weakening thresholds.
- Electricity tariffs, currency, cumulative energy, and cost are not learned behavioral metrics. Rate changes never modify learned power baselines, and power-learning updates never trigger inference.
- Learned usage and power behavior supply deterministic next-observed-hour and evidence-covered end-of-day energy/cost forecasts. Missing future activity or power evidence remains missing and powered-off time is never extrapolated.
- Qwen receives only bounded precomputed forecast evidence. It may provide a concise interpretation but never authoritatively calculates forecast values, fills missing evidence, or changes posture, Learning, Insight priority, or actions.
- AI Outlook generation is authorized only when Overview is visible or by explicit refresh, uses a material-evidence runtime cache, and is never triggered by ambient telemetry alone.
- Insight eligibility, significance, deduplication, cooldown, and priority arbitration are deterministic; Qwen never decides whether an insight should surface.
- Insight arbitration is reserved for noteworthy evidence; routine persistent status does not compete for the Local Insight slot.
- Controlled actions require an exact reviewed plan and immediate explicit user approval. Qwen never creates, alters, approves, or executes mutation parameters.
- Action execution revalidates the reviewed precondition, routes only to a fixed allowlisted provider, persists recovery before mutation, and independently verifies the post-state.
- Undo is a separate explicitly approved, conflict-aware, revalidated, and verified action. Startup changes never stop or launch the target process.
- Startup Management v1 mutates only supported current-user HKCU Run text values and direct regular current-user Startup-folder files. The manifest unvirtualizes only the fixed Run key on supported Windows builds; older builds, system scope, unsupported formats, and Matasuri's own presence remain read-only.
- Normal development deployment updates the package in place. Any destructive package operation requires a verified external state backup first, and rejected persistence is preserved with writes blocked rather than overwritten.
- Learned-energy deviation insights require Established matching power evidence, complete same-duration coverage, current evidence, and a conservative material deviation beyond the learned range.
- Insight availability never modifies machine-health posture. `New Insight` is a presence modifier, not a severity.
- The ambient orb uses one monotonic animation timeline; posture, hover, New Insight, and Generating are smoothly interpolated modifiers over its physically deforming breath rather than separate clips.
- Fullscreen suppression is presentation policy scoped to the orb's actual monitor. It never changes posture or Learning, other-monitor fullscreen remains non-suppressing, hidden rasterization stops, and logical phase continues.
- Reduced Motion uses static organic geometry with no continuous render timer. Orb rendering stays in `Machine.App/Ambient` and requires no runtime network or download.
- Learned watts remain the persistent behavioral fact. Cost per observed hour is a disposable projection from learned typical watts and the current applicable published rate; it is never persisted as Learning evidence.
- Today learned-normal projection integrates learned hour-by-Active/Idle power over actually observed persisted History durations. Incomplete learned duration coverage must never produce above/below-normal claims, and the projection never triggers inference.
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
