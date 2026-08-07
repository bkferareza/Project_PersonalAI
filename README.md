# Machine

`Machine` is the temporary working name for this application.

## Technology stack

C#, .NET 10, WinUI 3, Windows App SDK, and xUnit.

## Current status

The ambient Windows presence now idles as a dedicated 128×128 native layered window using per-pixel premultiplied-BGRA alpha through `UpdateLayeredWindow` and `ULW_ALPHA`. Its transparent corners reveal the desktop directly; only the cool white/cyan/blue-violet breathing energy core is visible. Forty-eight precomputed frames advance at 10 FPS over a 4.8-second loop, with an asymmetric energy body, soft transparent glow, and one faint broken arc. Only visible alpha pixels accept hover or click, and a click opens the unchanged dashboard; Back restores the orb at the existing DPI-aware bottom-right inset. The companion window is always-on-top, avoids dashboard chrome while idle, and disposes its native surface and animation timer during shutdown. Searchable inventories remain read-only. Deterministic findings stay authoritative, while short local insights are generated proactively after stabilized state or finding changes, recovery, or the first dashboard open for a verified context. Context fingerprints, a two-minute automatic cooldown, one-request concurrency, and safe deterministic fallback prevent duplicate or ungrounded output; process telemetry, raw inventories, startup names, commands, and paths are never sent to the model.

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

Complete the living-orb state and interaction choreography.
