# Machine

`Machine` is the temporary working name for this application.

## Technology stack

C#, .NET 10, WinUI 3, Windows App SDK, and xUnit.

## Current status

The dedicated 96×96 native layered orb retains its accepted ambient visuals and cheap reduced-motion behavior. Machine now probes local Ollama at startup; it reuses a healthy service or starts its own local `ollama serve` only when the executable is already installed, then waits for `/api/version`. It never downloads Ollama or a model, pulls a model, or loads `qwen3.5:4b` until a justified insight needs inference. A Machine-owned runtime is stopped at application shutdown; a pre-existing runtime is never terminated.

Telemetry records a compact local learning observation at most every 30 seconds from verified CPU, memory, Active/Idle input state, deterministic findings, and available system-volume capacity. Raw observations are bounded to 2,880 in memory and never persist. Welford baselines are maintained separately by local hour and Active/Idle state, progressing from Calibrating to Provisional after 12 samples and Established only after 168 samples spanning at least seven days. Context changes form bounded aggregate episodes. Only baseline aggregates and the latest 200 episodes are versioned and atomically persisted under Machine's local application data; corrupt data is ignored safely. Established learned context can enrich an already-authorized local insight, while deterministic findings remain authoritative and learned values never create severity, anomaly, cause, or action claims.

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

Complete read-only Windows observability v1 and feed new verified signals into local learning.
