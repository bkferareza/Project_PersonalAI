"""Merge a validated local Matasuri adapter into its pinned base model."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import torch
from peft import PeftModel
from safetensors import safe_open
from safetensors.torch import save_file
from transformers import AutoModelForImageTextToText, AutoTokenizer


def preserve_prediction_head(source: Path, output: Path) -> None:
    """Keep Qwen3.5's non-LoRA MTP tensors omitted by HF merged saving."""
    source_index_path = source / "model.safetensors.index.json"
    output_index_path = output / "model.safetensors.index.json"
    source_index = json.loads(source_index_path.read_text(encoding="utf-8"))
    output_index = json.loads(output_index_path.read_text(encoding="utf-8"))
    output_keys = set(output_index["weight_map"])
    missing = {
        key: shard
        for key, shard in source_index["weight_map"].items()
        if key.startswith("mtp.") and key not in output_keys
    }
    if not missing:
        return

    tensors = {}
    for shard in sorted(set(missing.values())):
        with safe_open(source / shard, framework="pt", device="cpu") as reader:
            for key in sorted(key for key, value in missing.items()
                              if value == shard):
                tensors[key] = reader.get_tensor(key).clone()
    shard_name = "model-mtp.safetensors"
    save_file(tensors, output / shard_name)
    output_index["weight_map"].update(
        {key: shard_name for key in tensors}
    )
    output_index.setdefault("metadata", {})["total_size"] = (
        int(output_index.get("metadata", {}).get("total_size", 0))
        + sum(tensor.numel() * tensor.element_size()
              for tensor in tensors.values())
    )
    output_index_path.write_text(
        json.dumps(output_index, indent=2) + "\n", encoding="utf-8"
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--adapter", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    model = AutoModelForImageTextToText.from_pretrained(
        args.model,
        local_files_only=True,
        trust_remote_code=False,
        dtype=torch.bfloat16,
        device_map={"": "cpu"},
    )
    adapted = PeftModel.from_pretrained(model, args.adapter,
                                        local_files_only=True)
    merged = adapted.merge_and_unload(safe_merge=True)
    args.output.mkdir(parents=True, exist_ok=True)
    merged.save_pretrained(args.output, safe_serialization=True,
                           max_shard_size="4GB")
    preserve_prediction_head(args.model, args.output)
    AutoTokenizer.from_pretrained(
        args.model, local_files_only=True, trust_remote_code=False
    ).save_pretrained(args.output)


if __name__ == "__main__":
    main()
