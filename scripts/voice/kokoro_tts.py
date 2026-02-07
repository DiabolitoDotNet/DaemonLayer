#!/usr/bin/env python3
import argparse
import os
import sys


def main() -> int:
    parser = argparse.ArgumentParser(description="Kokoro-82M TTS helper")
    parser.add_argument("--text", required=True, help="Text to synthesize")
    parser.add_argument("--output", required=True, help="Output WAV path")
    parser.add_argument("--voice", default=os.getenv("KOKORO_VOICE", "af_heart"))
    parser.add_argument("--lang_code", default=os.getenv("KOKORO_LANG_CODE", "a"))
    parser.add_argument("--sample_rate", type=int, default=int(os.getenv("KOKORO_SAMPLE_RATE", "24000")))

    args = parser.parse_args()

    text = args.text.strip()
    if not text:
        print("Text is empty", file=sys.stderr)
        return 2

    out_path = args.output
    out_dir = os.path.dirname(out_path) or "."
    os.makedirs(out_dir, exist_ok=True)

    try:
        import numpy as np  # type: ignore
        import soundfile as sf  # type: ignore
        from kokoro import KPipeline  # type: ignore
    except Exception as ex:  # pragma: no cover
        print(f"Failed to import kokoro dependencies: {ex}", file=sys.stderr)
        return 3

    try:
        pipeline = KPipeline(lang_code=args.lang_code)
        chunks = []
        generator = pipeline(text, voice=args.voice)
        for _, _, audio in generator:
            if audio is None:
                continue
            chunks.append(audio)

        if not chunks:
            print("No audio produced", file=sys.stderr)
            return 1

        audio_all = np.concatenate(chunks)
        sf.write(out_path, audio_all, args.sample_rate)

        if not os.path.exists(out_path) or os.path.getsize(out_path) <= 0:
            print("Output file not produced", file=sys.stderr)
            return 1

        return 0
    except Exception as ex:
        print(f"TTS failed: {ex}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
