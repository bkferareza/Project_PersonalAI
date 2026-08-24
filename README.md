# Matasuri

Matasuri is a local-first Windows machine intelligence layer. It observes verified local state, remembers what happened through bounded history, learns only from repeated deterministic evidence, and uses an optional local model to explain that evidence. Normal operation requires no cloud inference, paid API, internet access, remote Ollama, or automatic model download.

`Machine.*` remains the internal project, assembly, namespace, solution, and process naming for now.

## Technology

C#, .NET 10, WinUI 3, Windows App SDK, and xUnit. The dependency direction remains `Machine.App -> Core, Windows, Ollama`, `Machine.Windows -> Core`, and `Machine.Ollama -> Core`.

## Architecture

The solution is organized by domain inside its existing assembly boundaries. `Machine.Core` owns platform-neutral observability contracts plus History, Learning, Intelligence, and Runtime policy. `Machine.Windows` owns deterministic Windows acquisition and isolates native interop. `Machine.Ollama` separates local runtime ownership from grounded explanation and payload construction. `Machine.App` composes the shell and lifecycle while feature views own their page presentation. Tests mirror these domains. New code belongs with its domain or feature instead of a project-wide technical-type bucket.

## Current experience

Matasuri opens from its accepted transparent 96x96 ambient orb into a compact frameless Mica shell. A custom text-first rail separates Now, Memory, Observe, and System. The shell atmosphere follows only the current deterministic Stable, Attention, Warning, Critical, or Unknown state; local insight generation adds a restrained overlay without replacing severity. Semantic colors are centralized, transitions take 700 ms, and reduced-motion mode changes appearance immediately. The orb keeps native layered-window transparency, low-cost `WM_TIMER` cadence, its five-second Stable breath, and phase-preserving hover behavior.

Matasuri is a single resident instance. Its packaged Windows startup task (`MatasuriStartup`) establishes the orb without opening the dashboard; a normal later launch redirects to that resident instance and summons the dashboard. Esc, the dashboard return control, standard window close, and four seconds of sustained dashboard focus loss return quietly to the orb. There is no product Exit command. Windows and the user remain authoritative through normal Startup settings, Task Manager, shutdown/restart, update, and uninstall controls.

Overview is a current machine brief. History answers what happened. Learning answers what repeated evidence allowed Matasuri to conclude. Provider pages expose deeper read-only evidence without turning the shell into a control surface.

Stable means no current machine-wide degradation was verified; it does not erase localized reliability findings, which remain visible in Health and History.

Reliability history remains localized by default. Global Attention requires a current deterministic signal: an application loop with fresh repeated failures in 30 minutes (15-minute freshness), a verified Windows Update error with three failures in 24 hours (four-hour freshness), or an unexpected shutdown in the last four hours. Historical update and shutdown evidence remains retained without pinning the live posture.

## History

Accepted observations enter history at most once every 30 seconds. Incremental typed rollups retain 5-minute data for 48 hours (maximum 576), hourly data for 90 days (2,160), daily data for 730 days, and monthly data for 120 months. Numeric values retain count/min/max/mean; activity and deterministic state retain observed durations. Offline and suspended time remains missing rather than becoming zero, Active, or Idle.

`matasuri-history-v1.json` is separate from behavioral learning. It uses schema v1, atomic replacement, bounded collections, a 10-minute dirty-save cadence with backoff, and a bounded final shutdown save. A sparse normalized timeline retains at most 2,000 significant events for 730 days, deduplicates verified health identities, and groups repeated display events without storing raw Event Log payloads. The History page uses 5-minute resolution for 24h, hourly resolution for 7d and 30d, and monthly resolution for All; gaps remain missing data.

History opens with a shared local-day projection of observed PC energy and estimated electricity cost. It combines accepted additive History energy with the one pending valid contribution without double counting, uses only the matching effective-month published rate, and survives session restarts. `Running bill today` presents the same deterministic PC-cost projection in Local Insight without waking the model; it is not a household utility bill.

## Learning and local explanation

Hierarchical behavioral learning uses schema v4. Its bounded RAM-only 30-second journal, 48 hour-by-Active/Idle baselines, compact profiles, recurring patterns, and 200 aggregate episodes remain independent from History. Schema v3 evidence migrates without reset. Each existing context can now accumulate separately mature software-estimated whole-PC wall-power statistics from eligible v4 observations; power is never backfilled from History, and powered-off or suspended gaps add no evidence.

Learned power represents machine behavior, not metered wall evidence. Low-confidence, unavailable, invalid, or stale estimates are excluded without rejecting the rest of an observation. Electricity tariffs, currency, cumulative energy, and cost remain outside Learning, so rate changes cannot alter learned power behavior. Power updates add no timer, network request, or inference trigger.

The Learning page derives current-context estimated electricity cost per observed hour from the adaptive learned wattage and the matching published residential reference rate. This monetary projection is never written into Learning. It also compares Today against learned normal by integrating each usable hour-by-Active/Idle power profile over the corresponding accepted History duration. The comparison uses the last fully accepted History checkpoint so actual energy and expected duration remain aligned across restarts; live pending Today energy continues separately in History, Hardware, and `Running bill today`. If any observed duration lacks at least Provisional power evidence, Learning reports factual coverage and stays in a Still Learning state instead of issuing an above/below-normal claim.

`learning-activity.json` is a separate bounded local diagnostic trail. It records safe lifecycle, restore, observation, and persistence summaries (not raw telemetry, process, URL, or command data), retains detailed observation events for 48 hours and important lifecycle events for 14 days, and cannot repair or block behavioral learning.

Ollama integration remains transitional and local. Matasuri reuses an existing healthy local service or starts an already-installed `ollama serve`; it never downloads Ollama or a model. `qwen3.5:4b` is demand-loaded only after the existing insight policy authorizes an explanation. History, inventory refresh, GPU polling, palette changes, and learning updates never trigger inference. An authorized request adds at most one current historical aggregate, one recent comparison, one event, and four nullable current GPU values; services, tasks, and devices are excluded from model context.

## Observability coverage

All capabilities below are read-only.

| Observability v1 | Status |
| --- | --- |
| Resources and top processes | Complete |
| Storage | Complete |
| Traditional and packaged software | Complete |
| Startup applications | Complete |
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

## Privacy and safety

History never stores process names, interface identities, addresses, endpoints, URLs, document or window titles, commands, task arguments, device serials, raw Event Log XML, dumps, or generated prose. Windows inventories use bounded normalized fields and have no start/stop, run/edit, enable/disable, install, registry-write, hardware-tuning, or power-setting operations.

## Debugging in VS Code

Install the .NET 10 SDK plus the workspace-recommended Microsoft C# Dev Kit and WinApp extensions. Select `Machine.App: Debug x64`; the workspace task builds and registers the development package, the debugger attaches to the internal `Machine.App` process, and the post-debug task removes the package. If cleanup is interrupted, run `Machine.App: Remove debug package` before the next session.

## Next slice

Use Established learned-energy behavior to surface a conservative deterministic Local Insight only when Today is meaningfully outside its learned normal range, without waking Qwen or changing machine posture.
