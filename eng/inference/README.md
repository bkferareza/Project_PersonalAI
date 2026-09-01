# Local inference artifacts

Matasuri pins the official llama.cpp `b10724` Windows x64 CUDA 12.4 release
and converts the official Qwen3.5-4B revision
`851bf6e806efd8d0a36b00ddf55e13ccb7b8cd0a` to a verified Q4_K_M GGUF.
Large archives, extracted native files, source weights, and converted models
are deliberately excluded from Git.

Developer preparation is explicit and separate from build/runtime:

```powershell
.\eng\inference\Prepare-LocalInference.ps1
.\eng\inference\Build-QwenModel.ps1
.\eng\inference\Prepare-LocalInference.ps1 -SkipRuntimeDownload -StagePreparedModel
```

The first command downloads only the two pinned official llama.cpp archives,
checks their hashes, and stages native runtime files under the ignored
`artifacts/local-inference` directory. The model-build command explicitly
checks out the pinned official llama.cpp and Qwen revisions, verifies the
three large source artifacts, converts to BF16, and quantizes to Q4_K_M. The
final command copies only that manifest-verified result into
`%LOCALAPPDATA%\Matasuri\Inference\Models`.

Application startup and normal inference never run these scripts, access an
Ollama model directory, or download runtime/model artifacts.
