# Voice interface (Whisper.cpp STT + Piper.Net TTS, CPU-only)

This project is local-first. The voice pipeline is **disabled by default** and must be explicitly enabled in configuration.

## STT: Whisper.cpp (CPU-only)

1. Download/build `whisper.cpp` for Windows and locate the CLI executable (commonly `whisper-cli.exe`).
2. Download a GGML model (e.g., `ggml-base.en.bin`) and place it somewhere local.
3. Configure the STT tool:

- `VoiceTranscription:Enabled=true`
- `VoiceTranscription:ExecutablePath` = full path to `whisper-cli.exe`
- `VoiceTranscription:Arguments` should include:
  - `-ngl 0` to force CPU-only (no GPU layers)
  - `-f {input}` where `{input}` is substituted by the tool
  - Optional: `-nt` (no timestamps), `-np` (no progress)

Example `appsettings.json` snippet:

```json
{
  "Voice": { "Enabled": true, "LocalOnly": true },
  "VoiceTranscription": {
    "Enabled": true,
    "ExecutablePath": "C:/tools/whisper/whisper-cli.exe",
    "RootDirectory": "data/voice",
    "Arguments": ["-m", "C:/tools/whisper/models/ggml-base.en.bin", "-nt", "-np", "-ngl", "0", "-f", "{input}"]
  }
}
```

## TTS: Piper.Net (CPU-only)

TTS is implemented in-process using `LMSupply.Synthesizer` (Piper/VITS ONNX).

1. Download a Piper-compatible **voice** (typically an ONNX model + config) and place it in a local directory.
2. Configure the TTS tool:

- `TextToSpeech:Enabled=true`
- `TextToSpeech:UsePiperNet=true`
- `TextToSpeech:PiperVoicePath` = the voice directory (or alias supported by the synthesizer)
- Optional: `TextToSpeech:PiperSpeakerId` for multi-speaker voices
- Optional: `TextToSpeech:PiperThreadCount` (0 = auto)

Example:

```json
{
  "Voice": { "Enabled": true, "LocalOnly": true },
  "TextToSpeech": {
    "Enabled": true,
    "UsePiperNet": true,
    "PiperVoicePath": "C:/voices/en_US-lessac",
    "PiperSpeakerId": 0,
    "PiperSpeed": 1.0,
    "PiperThreadCount": 0,
    "RootDirectory": "data/voice",
    "OutputExtension": ".wav"
  }
}
```

## Host API endpoints

When `Voice:Enabled=true`:

- `POST /api/voice/transcribe` (multipart/form-data, `file`)
- `POST /api/voice/speak` (JSON `{ "text": "..." }`) → returns an audio file (WAV by default)

These endpoints are **local-only by default**: set `Voice:LocalOnly=false` to allow non-loopback clients.
