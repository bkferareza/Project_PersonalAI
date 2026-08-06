# Machine

`Machine` is the temporary working name for this application.

## Technology stack

C#, .NET 10, WinUI 3, Windows App SDK, and xUnit.

## Current status

The ambient Windows presence idles as a captionless 96×96 living orb on a native Desktop Acrylic surface, leaving its surrounding content visually transparent and showing no text, telemetry, card, badge, or command. Its layered cool white/cyan/blue-violet core has a translucent energy layer, faint rings, and soft glow. Mouse or pen hover and keyboard focus reveal a 280×100 context view with the verified state, latest insight, and CPU and memory telemetry while preserving the bottom-right anchor; clicking anywhere on the surface or pressing Enter or Space opens the navigable expanded dashboard, and Back returns to the orb. State-specific motion uses low-cost opacity, scale, rotation, and translation animations: slow breathing/ring drift when stable, progressively stronger controlled pulses for escalating states, an internal sweep during generation, and a one-time bloom for a new insight. It honors the Windows reduced-motion setting and stops during shutdown. Searchable inventories remain read-only. Deterministic findings stay authoritative, while short local insights are generated proactively after stabilized state or finding changes, recovery, or the first dashboard open for a verified context. Context fingerprints, a two-minute automatic cooldown, one-request concurrency, and safe deterministic fallback prevent duplicate or ungrounded output; process telemetry, raw inventories, startup names, commands, and paths are never sent to the model.

## Product direction

Machine is a local-first Windows intelligence layer that observes, explains, and eventually performs controlled actions on the computer through deterministic Windows capabilities, with Ollama used later as an optional natural-language and personality layer.

## Debugging in VS Code

Install the .NET 10 SDK plus the workspace-recommended Microsoft C# Dev Kit and WinApp extensions. Open this repository folder in VS Code, then:

1. Open `src/Machine.App/MainWindow.xaml.cs` and set a breakpoint in application code, such as the first line of `OnCompactPresenceTapped`.
2. In Run and Debug, select `Machine.App: Debug x64` and press F5.
3. The pre-launch task builds `Machine.App` in Debug for x64. The WinApp extension registers and launches the packaged app, then attaches the managed C# debugger with application symbols.
4. Trigger the breakpoint by activating the compact presence surface, then continue debugging normally.
5. Press Shift+F5 to stop. The post-debug task unregisters the development package and verifies that no `Machine.App` process remains.

If a debug session is interrupted before cleanup runs, use **Terminal > Run Task > Machine.App: Remove debug package** before starting another session. This workflow does not require Visual Studio and does not claim XAML Hot Reload, Live Visual Tree, or designer support.

## Next slice

Harden natural Taglish insight quality and reduce deterministic fallback frequency.
