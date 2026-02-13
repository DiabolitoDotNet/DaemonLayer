# Voice interface (Faster-Whisper STT + Kokoro-82M TTS, local-first)

This project is local-first.

- In application defaults, the voice pipeline is **disabled by default** and must be explicitly enabled in configuration.
- In Docker, the provided `docker-compose.yml` enables the voice endpoints by default. Models are downloaded on first use and cached under `./models/hf`.

## STT: Faster-Whisper (large-v3-turbo, CPU)

In Docker, STT is implemented by running a small Python helper that uses `faster-whisper` (CTranslate2). The model is downloaded on first use and cached under `./models/hf`.

1. Ensure Python is available.
2. Install dependencies: `pip install faster-whisper`
3. Configure the STT tool:

- `VoiceTranscription:Enabled=true`
- `VoiceTranscription:ExecutablePath` = `python3` (or `python.exe` on Windows)
- `VoiceTranscription:Arguments` should include:
  - the helper script path
  - `--input {input}` where `{input}` is substituted by the tool
  - `--model large-v3-turbo`

Example `appsettings.json` snippet:

```json
{
  "Voice": { "Enabled": true, "LocalOnly": true },
  "VoiceTranscription": {
    "Enabled": true,
    "ExecutablePath": "python",
    "RootDirectory": "data/voice",
    "Arguments": ["C:/path/to/faster_whisper_transcribe.py", "--input", "{input}", "--model", "large-v3-turbo", "--device", "cpu", "--compute_type", "int8", "--language", "en"]
  }
}
```

## TTS: Kokoro-82M (CPU)

In Docker, TTS is implemented by running a small Python helper that uses the `kokoro` package. Model assets are downloaded on first use and cached under `./models/hf`.

1. Ensure Python is available.
2. Install dependencies: `pip install kokoro soundfile`
3. Configure the TTS tool:

- `TextToSpeech:Enabled=true`
- `TextToSpeech:UsePiperNet=false`
- `TextToSpeech:ExecutablePath` = `python3` (or `python.exe` on Windows)
- `TextToSpeech:Arguments` must include `{text}` and `{output}` placeholders

Example:

```json
{
  "Voice": { "Enabled": true, "LocalOnly": true },
  "TextToSpeech": {
    "Enabled": true,
    "UsePiperNet": false,
    "ExecutablePath": "python",
    "Arguments": ["C:/path/to/kokoro_tts.py", "--text", "{text}", "--output", "{output}", "--voice", "ff_siwis", "--lang_code", "f", "--sample_rate", "24000"],
    "RootDirectory": "data/voice",
    "OutputExtension": ".wav"
  }
}
```

## Host API endpoints

When `Voice:Enabled=true`:

- `POST /api/voice/transcribe` (multipart/form-data, `file`)
- `POST /api/voice/speak` (JSON `{ "text": "..." }`) → returns an audio file (WAV by default)
- `POST /api/voice/copilot` (JSON `{ "text": "...", "sessionId": "...", "speak": false }`) → returns `{ sessionId, reply, speechText, ttsEnqueued }`

These endpoints are **local-only by default**: set `Voice:LocalOnly=false` to allow non-loopback clients.

Quick smoke test (recommended in Docker with `Voice:LocalOnly=false`):

```bash
curl -H "Content-Type: application/json" \
  -d '{"text":"Bonjour","sessionId":"demo","speak":false}' \
  http://localhost:5080/api/voice/copilot
```

## Troubleshooting

- If `POST /api/voice/speak` or `/api/voice/transcribe` returns HTTP 500 in Docker right after enabling voice, run:

  - `docker compose logs infernal-hierarchy --tail 200`

  Most issues are missing dependencies (Python packages) or missing outbound connectivity for the first model download.

- To prefetch models into `./models/hf` (best-effort):

  - `docker compose exec infernal-hierarchy /opt/voice-venv/bin/python /app/voice/download_voice_models.py`
