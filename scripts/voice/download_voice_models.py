#!/usr/bin/env python3
"""Best-effort prefetch for Faster-Whisper + Kokoro.

This script is meant to be run inside the container so model artifacts land in the
mounted HuggingFace cache directory.

Usage:
  python3 /app/voice/download_voice_models.py
"""

import os
import sys


def main() -> int:
    faster_model = os.getenv("FASTER_WHISPER_MODEL", "large-v3-turbo")
    download_root = os.getenv("FASTER_WHISPER_DOWNLOAD_ROOT", "/models/hf/faster-whisper")

    print(f"Prefetching faster-whisper model: {faster_model}")
    os.makedirs(download_root, exist_ok=True)

    try:
        from faster_whisper import WhisperModel  # type: ignore

        _ = WhisperModel(
            faster_model,
            device=os.getenv("FASTER_WHISPER_DEVICE", "cpu"),
            compute_type=os.getenv("FASTER_WHISPER_COMPUTE_TYPE", "int8"),
            download_root=download_root,
        )
        print("faster-whisper: OK")
    except Exception as ex:
        print(f"faster-whisper prefetch failed: {ex}")

    print("Prefetching kokoro assets (best-effort)...")
    try:
        from kokoro import KPipeline  # type: ignore

        pipeline = KPipeline(lang_code=os.getenv("KOKORO_LANG_CODE", "a"))
        # Trigger internal lazy loads.
        _ = list(pipeline("Hello", voice=os.getenv("KOKORO_VOICE", "af_heart")))
        print("kokoro: OK")
    except Exception as ex:
        print(f"kokoro prefetch failed: {ex}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
