# Matasuri

Matasuri is a local-first Windows machine intelligence layer. It observes verified local state, remembers what happened through bounded history, learns only from repeated deterministic evidence, and uses an optional local model to explain that evidence. Normal operation requires no cloud inference, paid API, internet access, remote Ollama, or automatic model download.

`Machine.*` remains the internal project, assembly, namespace, solution, and process naming for now.

## Technology

C#, .NET 10, WinUI 3, Windows App SDK, and xUnit. The dependency direction remains `Machine.App -> Core, Windows, Ollama`, `Machine.Windows -> Core`, and `Machine.Ollama -> Core`.

## Architecture

The solution is organized by domain inside its existing assembly boundaries. `Machine.Core` owns platform-neutral observability contracts plus History, Learning, Intelligence, and Runtime policy. `Machine.Windows` owns deterministic Windows acquisition and isolates native interop. `Machine.Ollama` separates local runtime ownership from grounded explanation and payload construction. `Machine.App` composes the shell and lifecycle while feature views own their page presentation. Tests mirror these domains. New code belongs with its domain or feature instead of a project-wide technical-type bucket.

## Current experience

Matasuri opens from its accepted transparent 96x96 ambient orb into a compact frameless Mica shell. A custom text-first rail separates Now, Memory, Observe, and System. The shell atmosphere follows only the current deterministic Stable, Attention, Warning, Critical, or Unknown state; local insight generation adds a restrained overlay without replacing severity. Semantic colors are centralized, transitions take 700 ms, and reduced-motion mode changes appearance immediately. The orb keeps native layered-window transparency, low-cost `WM_TIMER` cadence, its five-second Stable breath, and phase-preserving hover behavior.

Overview is a current machine brief. History answers what happened. Learning answers what repeated evidence allowed Matasuri to conclude. Provider pages expose deeper read-only evidence without turning the shell into a control surface.

## History

Accepted observations enter history at most once every 30 seconds. Incremental typed rollups retain 5-minute data for 48 hours (maximum 576), hourly data for 90 days (2,160), daily data for 730 days, and monthly data for 120 months. Numeric values retain count/min/max/mean; activity and deterministic state retain observed durations. Offline and suspended time remains missing rather than becoming zero, Active, or Idle.

`matasuri-history-v1.json` is separate from behavioral learning. It uses schema v1, atomic replacement, bounded collections, a 10-minute dirty-save cadence with backoff, and a bounded final shutdown save. A sparse normalized timeline retains at most 2,000 significant events for 730 days, deduplicates verified health identities, and groups repeated display events without storing raw Event Log payloads. The History page uses 5-minute resolution for 24h, hourly resolution for 7d and 30d, and monthly resolution for All; gaps remain missing data.

## Learning and local explanation

Hierarchical behavioral learning remains schema v3. Its bounded RAM-only 30-second journal, 48 hour-by-Active/Idle baselines, compact profiles, recurring patterns, and 200 aggregate episodes remain independent from History. Powered-off and suspended gaps add no learning evidence.

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
| CPU hardware sensors | Planned |
| Storage SMART and temperature | Planned |
| Power, energy, and cost | Planned |

The GPU slice reads adapter name, utilization, VRAM, temperature, board power, graphics/memory clocks, and fan when the installed driver exposes them. Unsupported or unavailable values remain null; Matasuri adds no NVML package, download, control call, tuning, or severity policy.

## Privacy and safety

History never stores process names, interface identities, addresses, endpoints, URLs, document or window titles, commands, task arguments, device serials, raw Event Log XML, dumps, or generated prose. Windows inventories use bounded normalized fields and have no start/stop, run/edit, enable/disable, install, registry-write, hardware-tuning, or power-setting operations.

## Debugging in VS Code

Install the .NET 10 SDK plus the workspace-recommended Microsoft C# Dev Kit and WinApp extensions. Select `Machine.App: Debug x64`; the workspace task builds and registers the development package, the debugger attaches to the internal `Machine.App` process, and the post-debug task removes the package. If cleanup is interrupted, run `Machine.App: Remove debug package` before the next session.

## Next slice

Add CPU/storage hardware telemetry and estimated power, energy, and electricity-cost observability.
