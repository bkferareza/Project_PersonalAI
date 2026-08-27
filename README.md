# Matasuri

Matasuri is a local-first Windows machine intelligence layer. It observes verified local state, remembers what happened through bounded history, learns only from repeated deterministic evidence, and uses an optional local model to explain that evidence. Normal operation requires no cloud inference, paid API, internet access, remote Ollama, or automatic model download.

`Machine.*` remains the internal project, assembly, namespace, solution, and process naming for now.

## Technology

C#, .NET 10, WinUI 3, Windows App SDK, and xUnit. The dependency direction remains `Machine.App -> Core, Windows, Ollama`, `Machine.Windows -> Core`, and `Machine.Ollama -> Core`.

## Architecture

The solution is organized by domain inside its existing assembly boundaries. `Machine.Core` owns platform-neutral observability and controlled-action contracts plus History, Learning, Intelligence, and Runtime policy. `Machine.Windows` owns deterministic Windows acquisition, the narrow allowlisted Startup mutations, and native interop. `Machine.Ollama` separates local runtime ownership from grounded explanation and payload construction. `Machine.App` composes the shell and lifecycle while feature views own their page presentation. Tests mirror these domains. New code belongs with its domain or feature instead of a project-wide technical-type bucket.

## Current experience

Matasuri opens from its transparent 96x96 ambient orb into a compact frameless Mica shell. A custom text-first rail separates Now, Memory, Observe, and System. The shell atmosphere follows only the current deterministic Stable, Attention, Warning, Critical, or Unknown state; insight availability never replaces severity. The native layered-window orb now renders directly into one reused BGRA buffer at 20 FPS. Its five-second Stable breath physically expands and reshapes a smooth asymmetric harmonic contour while internal light and membrane depth move with it; hover preserves elapsed phase, hidden rendering stops, and Reduced Motion uses one static organic frame.

Matasuri is a single resident instance. Its packaged Windows startup task (`MatasuriStartup`) establishes the orb without opening the dashboard; a normal later launch redirects to that resident instance and summons the dashboard. Esc, the dashboard return control, standard window close, and four seconds of sustained dashboard focus loss return quietly to the orb. There is no product Exit command. Windows and the user remain authoritative through normal Startup settings, Task Manager, shutdown/restart, update, and uninstall controls.

Overview is a current machine brief. History answers what happened. Learning answers what repeated evidence allowed Matasuri to conclude. Provider pages expose deeper read-only evidence without turning the shell into a control surface.

Stable means no current machine-wide degradation was verified; it does not erase localized reliability findings, which remain visible in Health and History.

Reliability history remains localized by default. Global Attention requires a current deterministic signal: an application loop with fresh repeated failures in 30 minutes (15-minute freshness), a verified Windows Update error with three failures in 24 hours (four-hour freshness), or an unexpected shutdown in the last four hours. Historical update and shutdown evidence remains retained without pinning the live posture.

## History

Accepted observations enter history at most once every 30 seconds. Incremental typed rollups retain 5-minute data for 48 hours (maximum 576), hourly data for 90 days (2,160), daily data for 730 days, and monthly data for 120 months. Numeric values retain count/min/max/mean; activity and deterministic state retain observed durations. Offline and suspended time remains missing rather than becoming zero, Active, or Idle.

`matasuri-history-v1.json` is separate from behavioral learning. It uses schema v1, atomic replacement, bounded collections, a 10-minute dirty-save cadence with backoff, and a bounded final shutdown save. A sparse normalized timeline retains at most 2,000 significant events for 730 days, deduplicates verified health identities, and groups repeated display events without storing raw Event Log payloads. The History page uses 5-minute resolution for 24h, hourly resolution for 7d and 30d, and monthly resolution for All; gaps remain missing data.

History opens with a shared local-day projection of observed PC energy and estimated electricity cost. It combines accepted additive History energy with the one pending valid contribution without double counting, uses only the matching effective-month published rate, and survives session restarts. Overview presents `Running bill today` permanently in its own Today card beside Current Findings and Local Insight without waking the model; it is not a household utility bill.

Local Insight has one deterministic delivery path for noteworthy evidence. Current meaningful machine findings outrank an Established learned-energy deviation, which outranks routine information. The runtime-only arbiter rejects stale or insignificant candidates, deduplicates stable semantic identities, applies a six-hour repeat-signal cooldown, surfaces one current insight, and separately tracks whether it is new and unseen. Running Bill is not an `InsightCandidate`, never enters this ordering, and cannot signal New Insight. Opening Overview marks the current insight viewed. A one-shot organic wake plus a restrained static cue communicates New Insight without changing machine posture.

## Learning and local explanation

Hierarchical behavioral learning uses schema v4. Its bounded RAM-only 30-second journal, 48 hour-by-Active/Idle baselines, compact profiles, recurring patterns, and 200 aggregate episodes remain independent from History. Schema v3 evidence migrates without reset. Each existing context can now accumulate separately mature software-estimated whole-PC wall-power statistics from eligible v4 observations; power is never backfilled from History, and powered-off or suspended gaps add no evidence.

Learned power represents machine behavior, not metered wall evidence. Low-confidence, unavailable, invalid, or stale estimates are excluded without rejecting the rest of an observation. Electricity tariffs, currency, cumulative energy, and cost remain outside Learning, so rate changes cannot alter learned power behavior. Power updates add no timer, network request, or inference trigger.

The Learning page derives current-context estimated electricity cost per observed hour from the adaptive learned wattage and the matching published residential reference rate. This monetary projection is never written into Learning. It also compares Today against learned normal by integrating each usable hour-by-Active/Idle power profile over the corresponding accepted History duration. The comparison uses the last fully accepted History checkpoint so actual energy and expected duration remain aligned across restarts; live pending Today energy continues separately in History, Hardware, and `Running bill today`. If any observed duration lacks at least Provisional power evidence, Learning reports factual coverage and stays in a Still Learning state instead of issuing an above/below-normal claim.

The proactive learned-energy candidate is more conservative than the Learning presentation: every contributing power context must be Established, same-duration coverage must be complete, at least one hour must be observed, and actual energy must clear the nearest learned bound by both 0.010 kWh and 5%. Above and below use stable identities, so small value updates refresh evidence without becoming new events. Energy remains the evidence; matching-rate cost is optional supporting interpretation. A missing rate does not block the insight.

`learning-activity.json` is a separate bounded local diagnostic trail. It records safe lifecycle, restore, observation, and persistence summaries (not raw telemetry, process, URL, or command data), retains detailed observation events for 48 hours and important lifecycle events for 14 days, and cannot repair or block behavioral learning.

Ollama integration remains transitional and local. Matasuri reuses an existing healthy local service or starts an already-installed `ollama serve`; it never downloads Ollama or a model. `qwen3.5:4b` is demand-loaded only after the user explicitly presses `Explain`; candidate creation, arbitration, new/viewed state, orb animation, Running Bill updates, and learned-energy comparison never trigger inference. A learned-deviation explanation receives only the arbiter's bounded actual/expected energy, same-duration range, duration, coverage, Established maturity, difference, and optional rate-derived cost. Other authorized machine explanations retain their bounded existing context; services, tasks, and devices remain excluded.

## Observability coverage

All capabilities below remain read-only except the explicitly reviewed and reversible current-user Startup providers described in the controlled-action section.

| Observability v1 | Status |
| --- | --- |
| Resources and top processes | Complete |
| Storage | Complete |
| Traditional and packaged software | Complete |
| Startup applications | Complete inventory plus bounded current-user management |
| Network, uptime, and Active/Idle session state | Complete |
| Windows Update, reboot pending, and reliability | Complete |
| Services | Complete |
| Scheduled tasks | Complete |
| Devices and drivers | Complete |
| Suspend/resume boundaries | Complete |
| Local Ollama runtime | Complete |
| Historical layer | Complete |

`READ_ONLY_OBSERVABILITY_V1_COMPLETE`

| Observability v2 | Status |
| --- | --- |
| NVIDIA GPU telemetry through dynamically loaded local NVML | Initial implementation complete |
| CPU hardware metadata/frequency and package-power estimate | Initial implementation complete |
| Windows-reported physical storage health | Initial implementation complete |
| Estimated wall power and observed session energy | Initial implementation complete |
| Electricity-rate enrichment and bounded monthly cache | Initial implementation complete |
| Persistent energy and electricity cost | Initial implementation complete |

The GPU slice reads adapter name, utilization, VRAM, temperature, board power, graphics/memory clocks, and fan when the installed driver exposes them. Unsupported or unavailable values remain null; Matasuri adds no NVML package, download, control call, tuning, or severity policy.

CPU utilization remains sourced from the existing resource sampler. Safe Windows CPU metadata and effective frequency enrich it; CPU package temperature and measured package watts remain unavailable when no trustworthy source exists. CPU and whole-PC watts are clearly labeled estimates. Windows Storage Management supplies bounded physical-device identity and health state; missing reliability counters remain unavailable. Energy integrates estimated wall power only across short monotonic observed intervals, so suspend/offline gaps are excluded.

Electricity-rate enrichment is cache-first and optional. Its only external requests are HTTPS requests to the selected coarse-location endpoint and an official utility source, both enforced by a component-local allowlist with no redirects. Coarse location is used only in memory to resolve a probable utility; IP addresses, coordinates, location history, account information, machine data, History, and Learning data are neither sent nor persisted. A published rate is retained only when its effective month and source parse unambiguously; otherwise energy remains available and the rate stays unknown.

## Controlled actions and Startup Management v1

Matasuri now has one deterministic mutation capability: changing a supported current-user startup registration. The reusable Core path is plan -> explicit reviewed approval -> precondition re-read -> fixed executor routing -> durable in-progress recovery -> mutation -> independent Windows re-query -> verified outcome. A reversible change exposes a separate conflict-aware review and approval before undo. The local schema-v1 `matasuri-actions-v1.json` outcome store retains 300 resolved actions plus every unresolved recovery record; rejected or newer-schema action state blocks mutation rather than allowing an unrecorded change.

Startup v1 supports only String or ExpandString values in the fixed HKCU Run key and direct regular files in the current user's Startup folder. The package manifest excludes only that Run key from MSIX registry-write virtualization; Windows builds below the fine-grained exclusion floor keep it read-only instead of accepting an isolated package-hive mutation. A Run disable removes that exact external value after matching its exact name, kind, and unexpanded data; undo restores it only if the identity remains vacant. A Startup-folder disable moves the exact file, without deleting it, into Matasuri's user-local recovery staging; hash and destination conflicts are checked again before restore. HKLM Run, common Startup, unsupported registry kinds, reparse targets, and Matasuri's own startup presence remain visibly read-only. Disabling startup never stops a running process, and restoring it never launches one.

The Startup page shows current state, manageability, reasons for read-only classifications, an exact `Disable at startup` or `Restore at startup` affordance, a restrained review dialog, verified result language, and bounded recent action history. Qwen has no dependency in this path and cannot create, parameterize, approve, or execute an action.

## Privacy and safety

History never stores process names, interface identities, addresses, endpoints, URLs, document or window titles, commands, task arguments, device serials, raw Event Log XML, dumps, or generated prose. Windows inventories remain bounded. There is no generic command, registry, file, service, task, install, hardware-tuning, or power-setting executor; the only write path is the fixed, inventory-derived, explicitly approved, reversible Startup capability above.

## Debugging in VS Code

Install the .NET 10 SDK plus the workspace-recommended Microsoft C# Dev Kit and WinApp extensions. Select `Machine.App: Debug x64`; the workspace task builds and updates/registers the same development package identity in place, preserving package-local application data. Debug completion does not unregister or clean the package.

The former automatic post-debug unregister removed the package container and caused development Learning/History loss. The explicitly named destructive unregister task is now guarded: it gracefully shuts down the exact resident, creates and revalidates a SHA-256 manifest backup under `%LOCALAPPDATA%\Matasuri\DevelopmentBackups`, and aborts before unregister if any required durable JSON or referenced unresolved Startup recovery file is unreadable, incompatible, missing from the copy, or checksum-invalid. Restore is explicit, first snapshots current state, refuses schema downgrade, and never overwrites a conflicting recovery file. See `scripts/development/README.md`. At runtime, rejected or temporarily unreadable persistence files are retained as bounded diagnostic copies and their store instance blocks writes instead of overwriting the only evidence with an empty state.

## Next slice

Use action outcome memory and startup observability to measure whether an approved startup change produced a meaningful verified improvement, then add the first evidence-backed recommendation policy without allowing autonomous execution.
