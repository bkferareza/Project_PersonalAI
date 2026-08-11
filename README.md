# Machine

`Machine` is the temporary working name for this application.

## Technology stack

C#, .NET 10, WinUI 3, Windows App SDK, and xUnit.

## Current status

Machine now opens as a fixed, frameless Mica dashboard with no native title bar or outer border. Its minimal integrated top region supports DPI-aware dragging through the Windows App SDK non-client-region API, a subtle close action, Back navigation, and Esc return to ambient presence. Overview, Learning, Network, Storage, Software, Startup, and Runtime use the existing dashboard navigation. The frameless window explicitly uses DWM's small rounded-corner clip to avoid the diagonal Mica composition seam seen with the default clip.

The dedicated 96×96 native layered orb retains its accepted transparent visuals and five-second Stable breathing cycle. Its native window now owns cadence with a bounded `WM_TIMER`, so animation continues while the WinUI dashboard HWND is hidden; visibility, reduced-motion, mode changes, and disposal start or stop exactly one timer without hover affecting breathing phase.

Live telemetry now includes active non-loopback network interfaces, cumulative receive/send counters, aggregate rates calculated from successive samples using monotonic elapsed time, and a conservative Quiet/Light/Active classification. The dedicated Network page exposes only local interface metadata and aggregate activity—never addresses, remote endpoints, packet contents, or configuration actions. Verified Windows uptime, Machine process-session uptime, current Active/Idle input state, and idle duration are also shown. Sleep/resume session boundaries remain deferred.

Telemetry records a compact local learning observation at most every 30 seconds from verified CPU, memory, Active/Idle input state, deterministic findings, available system-volume capacity, and the aggregate network activity class/rates, regardless of dashboard visibility. Interface identity never enters learning. Raw observations are bounded to 2,880 in memory and never persist. Welford baselines remain independent by local hour and Active/Idle state, while privacy-safe network class counts persist in schema version 2 with safe version 1 migration. Baselines progress from Calibrating to Provisional after 12 samples and Established only after 168 samples across at least seven distinct observed local days. Hour, date, and activity changes close deterministic aggregate episodes.

The dedicated Learning page exposes current calibration, observed duration, journal capacity, scheduler counters, CPU/memory baselines, dominant network-class evidence after at least 12 valid samples, deterministic learned items, the latest 50 of up to 200 persisted episodes, persistence health, and verified Ollama/model residency. Dirty learned state is saved atomically on a 10-minute periodic interval, retained across restart, retried with backoff after failure, and saved once more during bounded shutdown. Corrupt persisted state is ignored safely and reported read-only for the session. Established learned context can enrich an already-authorized local insight with bounded network/session summaries, while learned values never create severity, anomaly, cause, intent, recommendation, or action claims.

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

Add Windows Update, reboot-pending, and reliability observability.

Remaining conceptual read-only observability v1 work includes Windows Update/reboot state, services, scheduled tasks, reliability/crash history, device/driver inventory, and a final coverage review.
