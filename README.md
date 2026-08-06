# Machine

`Machine` is the temporary working name for this application.

## Technology stack

C#, .NET 10, WinUI 3, Windows App SDK, and xUnit.

## Current status

The ambient Windows presence now includes searchable, read-only inventories of classic desktop software, current-user MSIX/AppX packages, and startup applications. Framework and resource packages are excluded from the packaged-application list, and installation source, update status, enabled state, and startup impact are not inferred.

## Product direction

Machine is a local-first Windows intelligence layer that observes, explains, and eventually performs controlled actions on the computer through deterministic Windows capabilities, with Ollama used later as an optional natural-language and personality layer.

## Next slice

Add verified storage, software, and startup context to machine explanations.
