# Matasuri local inference third-party notices

Matasuri's optional app-owned local inference bundle contains the components
listed below. Exact artifact sizes, SHA-256 hashes, and source revisions are
recorded in `runtime-manifest.json` and `model-manifest.json` beside this
notice. Normal application startup does not download these artifacts.

## llama.cpp

- Project: llama.cpp
- Source: https://github.com/ggml-org/llama.cpp
- Release: `b10724`
- Source revision: `2d8d612e4c68d3801e556a1b4a028f55ec33ecbb`
- License: MIT

Copyright (c) 2023-2026 The ggml authors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Qwen3.5-4B

- Project: Qwen3.5-4B
- Source: https://huggingface.co/Qwen/Qwen3.5-4B
- Source revision: `851bf6e806efd8d0a36b00ddf55e13ccb7b8cd0a`
- App-owned derivative: GGUF version 3, Q4_K_M quantization
- License: Apache License 2.0
- License text: https://www.apache.org/licenses/LICENSE-2.0

Copyright 2026 Alibaba Cloud

The model artifact is converted from the pinned official source weights with
the pinned llama.cpp converter, then quantized with the pinned llama.cpp
quantizer. The conversion recipe and every source/model hash are recorded in
`model-manifest.json`.

## NVIDIA CUDA runtime components

The Windows x64 CUDA bundle includes `cudart64_12.dll`, `cublas64_12.dll`, and
`cublasLt64_12.dll`. They are staged unchanged from the official llama.cpp
release companion archive:

`cudart-llama-bin-win-cuda-12.4-x64.zip`

Source: https://github.com/ggml-org/llama.cpp/releases/download/b10724/cudart-llama-bin-win-cuda-12.4-x64.zip

The archive's exact size and SHA-256 are recorded in
`runtime-manifest.json`. These NVIDIA components are governed by the NVIDIA
CUDA Toolkit End User License Agreement and applicable redistributable terms:
https://docs.nvidia.com/cuda/eula/index.html

Matasuri does not redistribute an NVIDIA display driver; a compatible
installed NVIDIA driver is a runtime prerequisite for this CUDA build.

## LLVM OpenMP runtime

The llama.cpp bundle includes `libomp.dll`. Its accompanying
`LICENSE-LLVM-OpenMP` file is retained alongside the native runtime and is
included in the application output. It is licensed under Apache License 2.0
with LLVM exceptions as stated in that file.
