# Machine

`Machine` is the temporary working name for this application.

## Technology stack

C#, .NET 10, WinUI 3, Windows App SDK, and xUnit.

## Current status

Machine now opens as a fixed, frameless Mica dashboard with no native title bar or outer border. Its minimal integrated top region supports DPI-aware dragging through the Windows App SDK non-client-region API, a subtle close action, Back navigation, and Esc return to ambient presence. Overview, Learning, Network, Health, Storage, Software, Startup, and Runtime use the existing dashboard navigation. The frameless window explicitly uses DWM's small rounded-corner clip to avoid the diagonal Mica composition seam seen with the default clip.

The dedicated 96×96 native layered orb retains its accepted transparent visuals and five-second Stable breathing cycle. Its native window now owns cadence with a bounded `WM_TIMER`, so animation continues while the WinUI dashboard HWND is hidden; visibility, reduced-motion, mode changes, and disposal start or stop exactly one timer without hover affecting breathing phase.

Live telemetry now includes active non-loopback network interfaces, cumulative receive/send counters, aggregate rates calculated from successive samples using monotonic elapsed time, and a conservative Quiet/Light/Active classification. The dedicated Network page exposes only local interface metadata and aggregate activity—never addresses, remote endpoints, packet contents, or configuration actions. Verified Windows uptime, Machine process-session uptime, current Active/Idle input state, and idle duration are also shown. Sleep/resume session boundaries remain deferred.

Telemetry records a compact local learning observation at most every 30 seconds from verified CPU, memory, Active/Idle input state, deterministic findings, available system-volume capacity, and aggregate network activity class/rates, regardless of dashboard visibility. Interface identity never enters learning. Layer 0 raw observations are bounded to 2,880 in memory and never persist. Hour, date, and activity changes close deterministic aggregate episodes, with only the latest 200 retained.

Long-term learning is cumulative across Machine restarts. Layer 1 keeps at most 48 hour-by-Active/Idle baselines with lifetime Welford evidence plus CPU and memory behavior adapted by a deterministic 21-day time-aware half-life. Confidence remains Calibrating, Provisional after 12 samples, or Established after 168 samples across at least seven distinct local days; separate Fresh, Aging, and Stale states describe recency. Lifetime history remains intact while adaptive estimates can shift toward recent verified behavior.

Layer 2 materializes at most one compact Provisional or Established profile per context, including bounded statistical ranges and aggregate network evidence but no raw samples. Layer 3 deterministically groups compatible adjacent non-stale Established hourly profiles into a small set of canonical recurring time windows, including intentional midnight wrapping. It uses transparent range-overlap and network-compatibility rules rather than model inference, clustering packages, embeddings, or semantic labels.

The dedicated Learning page presents lifetime accepted observations and observed duration across Machine sessions, compact context profiles, broader patterns, the bounded recent journal and episodes, persistence health, and verified Ollama/model residency. Powered-off time is not observed or treated as Idle. Offline gaps create no samples and do not extend an episode or observed duration.

Learning persistence schema version 3 stores only cumulative sufficient statistics, adaptive state, compact profiles, broader patterns, session metadata, and bounded episodes. It safely migrates schema versions 1 and 2, initializes missing adaptive state from their best available lifetime baseline evidence, saves atomically on the existing 10-minute dirty interval with failure backoff, and performs a bounded final shutdown save. Storage remains bounded rather than growing with every observation.

Windows health observability is read-only. A cached local Windows Update Agent search exposes explicit update state, pending and important counts, successful scan/install timestamps, and at most 30 normalized history entries without installing or downloading anything; normal refresh is limited to once every 45 minutes. Dedicated reboot-pending aggregation combines Windows Update, component servicing, pending-file-rename, and computer-rename evidence without modifying the registry or clearing flags.

Bounded reliability acquisition queries only selected structured Windows Event Log providers and event IDs across the last 30 days. It retains at most 100 normalized incidents, summarizes 24-hour, 7-day, and 30-day application crashes, hangs, update failures, hardware-error records, and unexpected shutdowns, and deterministically deduplicates related application-error/WER events and Kernel-Power/EventLog shutdown evidence. It does not retain raw event XML, messages, command lines, document paths, dump data, or other personal text.

The Health page presents Windows Update, restart evidence, recent reliability incidents, and recurring application-failure aggregates; Overview carries only a compact deterministic Health card. Conservative findings keep a routine pending restart informational, leave isolated historical incidents in history, and elevate only documented recent recurrence thresholds. A separate bounded `health-history-v1.json` app-local memory stores compact verified summaries and continuity counters without changing schema-v3 behavioral baselines, Layer 0 sampling, the 48 hour-by-Active/Idle contexts, or Layer 3 pattern clustering.

When the existing insight policy already authorizes local Qwen inference, the model receives only the current verified state, current context baseline, one matching compact profile, one matching broader pattern, up to two relevant episode summaries, and a tiny verified health summary: current update state/count, reboot status with at most four reason codes, source freshness/completeness metadata, 7-day reliability counts, one significant incident, and one recurring application aggregate. Learning and health refreshes never wake the model. The model does not train, store memory, choose statistics, form patterns, or execute actions, and learned values never create severity, anomaly, cause, intent, recommendation, or action claims.

Machine probes local Ollama at startup; it reuses a healthy service or starts its own local `ollama serve` only when the executable is already installed, then waits for `/api/version`. It never downloads Ollama or a model, pulls a model, or loads `qwen3.5:4b` until a justified insight needs inference. Ordinary deterministic learning performs no model call. A Machine-owned runtime is stopped at application shutdown; a pre-existing runtime is never terminated.

## Product direction

Machine is a local-first Windows intelligence layer that observes, explains, and eventually performs controlled actions on the computer through deterministic Windows capabilities, with Ollama used later as an optional natural-language and personality layer.

## Debugging in VS Code

Install the .NET 10 SDK plus the workspace-recommended Microsoft C# Dev Kit and WinApp extensions. Open this repository folder in VS Code, then:

1. Open `src/Machine.App/NativeAmbientOrbWindow.cs` and set a breakpoint in application code, such as the first line of `PresentCurrentFrame`.
2. In Run and Debug, select `Machine.App: Debug x64` and press F5.
3. The pre-launch task builds `Machine.App` in Debug for x64. The WinApp extension registers and launches the packaged app, then attaches the managed C# debugger with application symbols.
4. Trigger the breakpoint by activating the compact presence surface, then continue debugging normally.
5. Press Shift+F5 to stop. The post-debug task unregisters the development package and verifies that no `Machine.App` process remains.

If a debug session is interrupted before cleanup runs, use **Terminal > Run Task > Machine.App: Remove debug package** before starting another session. This workflow does not require Visual Studio and does not claim XAML Hot Reload, Live Visual Tree, or designer support.

## Next slice

Add read-only services and scheduled-task observability.

Remaining conceptual read-only observability v1 work includes services, scheduled tasks, device inventory, driver inventory, sleep/resume handling if still valuable, and a final coverage review.
