# Matasuri AI evaluation

This development-only tool evaluates a pinned local model against the same
Matasuri Brief contract, evidence payload, validator, repair policy, and
fallback policy used by the application. It does not benchmark general-purpose
chat or write to Learning, History, or action memory.

Run the current reference model:

```powershell
dotnet run --project eng/Machine.AiEvaluation --configuration Debug
```

Select another pinned model manifest explicitly:

```powershell
dotnet run --project eng/Machine.AiEvaluation --configuration Debug -- `
  --model qwen3.5-2b-vanilla
```

Reports are written under the ignored `artifacts/ai-evaluation` directory as
both JSON and Markdown. The 14 held-out scenarios cover normal operation,
localized and broader issues, Learning maturity and deviation, forecasts,
contextual power, self-health, verified actions, and competing signals.

Export the initial bounded specialization corpus:

```powershell
dotnet run --project eng/Machine.AiEvaluation --configuration Debug -- `
  --export-dataset --dataset-only
```

The exporter uses a deterministic split and emits only normalized evidence,
the required output contract, validated deterministic targets, target evidence
IDs, maturity/availability metadata, and provenance. It never reads raw
Learning or History persistence, arbitrary files, locations, network
addresses, prompts from users, secrets, or generated prose from the product.
