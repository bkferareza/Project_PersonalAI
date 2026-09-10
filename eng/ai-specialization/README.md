# Matasuri 2B specialization experiment

This tooling is development-only. Normal Matasuri operation never imports
Python, downloads a model, or loads an adapter.

The experiment uses the ignored deterministic dataset emitted by
`Machine.AiEvaluation`, official Qwen3.5-2B source weights pinned at revision
`15852e8c16360a2fea060d615a32b45270f8a8fc`, and a repo-local Python 3.11
virtual environment. It trains a conservative rank-8 NF4 QLoRA adapter with
batch size 1, gradient accumulation 16, sequence length 1536, gradient
checkpointing, seed 3512, and one half-epoch.

```powershell
artifacts/local-inference/tools/training/.venv/Scripts/python.exe `
  eng/ai-specialization/train_qwen35_2b_qlora.py `
  --model artifacts/local-inference/source/Qwen3.5-2B `
  --dataset artifacts/ai-evaluation/dataset `
  --output artifacts/local-inference/specialization/qwen3.5-2b-matasuri-v3 `
  --epochs 0.5 `
  --max-seq-length 1536
```

The adapter and training manifest remain ignored artifacts. After successful
training, merge the adapter without modifying the pinned source model:

```powershell
artifacts/local-inference/tools/training/.venv/Scripts/python.exe `
  eng/ai-specialization/merge_adapter.py `
  --model artifacts/local-inference/source/Qwen3.5-2B `
  --adapter artifacts/local-inference/specialization/qwen3.5-2b-matasuri-v3/adapter `
  --output artifacts/local-inference/specialization/qwen3.5-2b-matasuri-v3/merged
```

The pinned llama.cpp conversion and quantization tools then produce a separate
GGUF candidate for evaluation. No teacher-generated response is accepted as
training truth unless the same deterministic structure and grounding
validation used by the product passes; this initial corpus deliberately uses
deterministic accepted targets. Every generated example is a distinct synthetic
variant. The canonical checked-in 14-case evaluation corpus remains outside all
dataset splits and is used only for final held-out behavior evaluation.
