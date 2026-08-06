# Machine

`Machine` is the temporary working name for this application.

## Technology stack

C#, .NET 10, WinUI 3, Windows App SDK, and xUnit.

## Current status

The ambient Windows presence includes searchable, read-only inventories of classic desktop software, current-user MSIX/AppX packages, and startup applications. A platform-neutral deterministic findings layer evaluates verified CPU, memory, system-volume free space, and partial-data state; it drives the compact presence state, a bounded expanded findings section, and authoritative context for user-triggered local explanations. Raw software inventories and startup commands or paths are not sent to the model.

## Product direction

Machine is a local-first Windows intelligence layer that observes, explains, and eventually performs controlled actions on the computer through deterministic Windows capabilities, with Ollama used later as an optional natural-language and personality layer.

## Debugging in VS Code

Install the .NET 10 SDK plus the workspace-recommended Microsoft C# Dev Kit and WinApp extensions. Open this repository folder in VS Code, then:

1. Open `src/Machine.App/MainWindow.xaml.cs` and set a breakpoint in application code, such as the first line of `OnDetailsToggleClicked`.
2. In Run and Debug, select `Machine.App: Debug x64` and press F5.
3. The pre-launch task builds `Machine.App` in Debug for x64. The WinApp extension registers and launches the packaged app, then attaches the managed C# debugger with application symbols.
4. Trigger the breakpoint, such as by selecting **Show details**, then continue debugging normally.
5. Press Shift+F5 to stop. The post-debug task unregisters the development package and verifies that no `Machine.App` process remains.

If a debug session is interrupted before cleanup runs, use **Terminal > Run Task > Machine.App: Remove debug package** before starting another session. This workflow does not require Visual Studio and does not claim XAML Hot Reload, Live Visual Tree, or designer support.

## Next slice

Harden natural Taglish explanations using deterministic findings.
