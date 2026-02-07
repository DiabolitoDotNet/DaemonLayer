#!/usr/bin/env python3
import argparse
import os
import sys


def main() -> int:
    parser = argparse.ArgumentParser(description="Faster-Whisper transcription helper")
    parser.add_argument("--input", required=True, help="Path to input audio file")
    parser.add_argument("--model", default=os.getenv("FASTER_WHISPER_MODEL", "large-v3-turbo"))
    parser.add_argument("--device", default=os.getenv("FASTER_WHISPER_DEVICE", "cpu"))
    parser.add_argument("--compute_type", default=os.getenv("FASTER_WHISPER_COMPUTE_TYPE", "int8"))
    parser.add_argument("--language", default=os.getenv("FASTER_WHISPER_LANGUAGE", "en"))
    parser.add_argument("--download_root", default=os.getenv("FASTER_WHISPER_DOWNLOAD_ROOT", "/models/hf/faster-whisper"))
    parser.add_argument("--beam_size", type=int, default=int(os.getenv("FASTER_WHISPER_BEAM_SIZE", "5")))
    parser.add_argument("--vad_filter", action="store_true", default=os.getenv("FASTER_WHISPER_VAD_FILTER", "1") not in ("0", "false", "False"))

    args = parser.parse_args()

    audio_path = args.input
    if not os.path.exists(audio_path):
        print(f"Input file not found: {audio_path}", file=sys.stderr)
        return 2

    try:
        from faster_whisper import WhisperModel  # type: ignore
    except Exception as ex:  # pragma: no cover
        print(f"Failed to import faster_whisper: {ex}", file=sys.stderr)
        return 3

    os.makedirs(args.download_root, exist_ok=True)

    try:
        model = WhisperModel(
            args.model,
            device=args.device,
            compute_type=args.compute_type,
            download_root=args.download_root,
        )

        segments, info = model.transcribe(
            audio_path,
            language=args.language if args.language else None,
            beam_size=args.beam_size,
            vad_filter=args.vad_filter,
        )

        text_parts = []
        for seg in segments:
            if seg.text:
                text_parts.append(seg.text.strip())

        transcript = " ".join([p for p in text_parts if p])
        # Output must be plain text for the .NET tool to capture.
        sys.stdout.write(transcript)
        return 0
    except Exception as ex:
        print(f"Transcription failed: {ex}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
