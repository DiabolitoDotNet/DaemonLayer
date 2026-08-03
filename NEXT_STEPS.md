# 🚀 Next Steps - Getting Advanced Features Running

## Update (Aug 2, 2026)

- Strict autonomy runtime blockers are closed.
- Autonomy certification hardening is implemented (readiness matrix, certification profile, terminal outcome contract, representative perf scenarios).
- Current validated baseline:
  - Strict Release build with analyzers is green.
  - Perf gate PASS including autonomy-focused scenarios.

### Current focus after closure

- Keep perf gate and regression gate green in CI.
- Treat this document as operational bring-up guidance; roadmap closure/progress is tracked in `COMPLETED.md`.

**Last Updated**: August 3, 2026  
**Status**: Solution builds in strict Release; advanced features are integrated and ready to run (some require local dependencies)

---

## ✅ What's Already Done

All advanced features have been **implemented and compile successfully**:
- ✅ Code quality analyzers (StyleCop, .NET analyzers)
- ✅ Vector search with Qdrant integration
- ✅ Multi-model LLM support (4 models)
- ✅ Token usage tracking and cost estimation
- ✅ Streaming LLM responses
- ✅ Event sourcing and time-travel debugging
- ✅ Memory pruning with archival
- ✅ Comprehensive configuration system

**Build Status**: ✅ Builds clean with strict analyzer-enabled Release gate

**Test Status**: ✅ `dotnet test -c Release` passes solution-wide (some integration-only tests are skipped by design)

---

## 🎯 What You Need To Do

### 1️⃣ **Configure Secrets** (REQUIRED)

- Set Telegram bot token (and optionally AllowedUserIds) using user-secrets or a secrets file.
- Ensure `src/InfernalHierarchy.Host/appsettings.json` has non-empty `Telegram:BotToken` at runtime.

---

### 2️⃣ **Start Ollama (Local)** (REQUIRED for LLM)

This repo expects Ollama to run locally on the host and the default model (`qwen3:8b`) to be available.

```bash
ollama list
```

**Time Required**: ~5-15 minutes depending on internet speed  
**Disk Space**: depends on quantization; expect a few GB for `qwen3:8b`

Verify:

```bash
curl.exe http://localhost:11434/api/tags
```

**Change model (local Ollama)**:

1) Pull the model locally:

```bash
ollama pull qwen3:8b
```

2) Update the app model in `docker-compose.yml`:
- `infernal-hierarchy.environment.Ollama__DefaultModel`

3) Verify:

```bash
curl.exe -s http://localhost:11434/api/tags
```

---

### 3️⃣ **Start Qdrant (Docker)** (REQUIRED for vector search)

```bash
# Start Qdrant container
docker compose up -d qdrant

# Verify it's running
curl.exe http://localhost:6333/collections
```

**Expected Response**: `{"result":{"collections":[]},"status":"ok","time":0.000...}`

**Disk Space**: Minimal (~100MB for Docker image)

**Tip**: The repo also ships a full-stack compose including the Host + Qdrant + SearXNG in `docker-compose.yml`.

**Ready check** (after starting the Host):

```bash
curl.exe http://localhost:5080/health/ready
```

Look for `qdrant` to be `Healthy`.

---

### 4️⃣ **Enable Advanced Features** (OPTIONAL)

Edit `src/InfernalHierarchy.Host/appsettings.json`:

```json
{
  "VectorMemoryOptions": {
    "Enabled": true,
    "QdrantUrl": "http://localhost:6333",
    "CollectionName": "infernal_facts",
    "VectorDimensions": 384
  },
  "MemoryPruningOptions": {
    "Enabled": true,  // ← Change from false
    "PruningIntervalHours": 24,
    "RetentionDays": 30,
    "MinConfidenceThreshold": 0.3,
    "EnableArchival": true
  }
}
```

**Note**: Features work independently. You can enable vector search without pruning, and vice versa.

---

### 4️⃣b **(Recommended) Enable ONNX Embeddings**

For higher-quality semantic search, enable ONNX embeddings and ensure model assets exist (see `models/README.md`):

```json
{
  "OnnxEmbeddingOptions": {
    "Enabled": true,
    "ModelPath": "./models/sentence-transformers/model.onnx",
    "TokenizerPath": "./models/sentence-transformers/tokenizer.json",
    "MaxSequenceLength": 128,
    "EmbeddingDimension": 384
  }
}
```

**Docker (recommended)**: keep the base compose safe-by-default, and enable ONNX via the provided override:

```bash
docker compose -f docker-compose.yml -f docker-compose.onnx.yml up -d
```

**What “good” looks like**:

- `GET /health/ready` includes an `onnx_embeddings` check with:
  - `status: Healthy`
  - `data.using_fallback: false`

If model/tokenizer files are present but the runtime fails to initialize, the check returns `Degraded` with `data.status: fallback`.

---

### 4️⃣d **(Operator) End-to-end Vector Smoke Test**

For a deterministic end-to-end check (index → search) without relying on LLM behavior, you can enable the operator-only endpoint:

1) Configure an operator key (user-secrets recommended):

```bash
dotnet user-secrets set "OperatorApi:Enabled" "true" --project src/InfernalHierarchy.Host
dotnet user-secrets set "OperatorApi:ApiKey" "<random-long-secret>" --project src/InfernalHierarchy.Host
```

2) Run the smoke test:

```bash
curl -H "X-Infernal-Operator-Key: <random-long-secret>" \
  -H "Content-Type: application/json" \
  -d "{\"content\":\"The capital of France is Paris.\",\"query\":\"What is the capital of France?\"}" \
  http://localhost:5080/api/ops/vector/smoke
```

Expected: JSON response with `hits` containing the inserted fact.

---

### 4️⃣c **(Optional) Enable Voice (STT/TTS) via Docker**

The embedded UI includes a Voice panel that calls:

- `POST /api/voice/transcribe` (STT)
- `POST /api/voice/speak` (TTS)

For interactive/local usage, the recommended “optimized enough” setup is:

- **TTS**: Kokoro-82M (Python)
- **STT**: Faster-Whisper `large-v3-turbo` (Python)

1) Start with an empty local cache directory (models download on first use):

- Hugging Face cache under `./models/hf`

2) Start the stack:

```bash
docker compose -f docker-compose.yml up -d --build
```

The base compose profile stays lean: no voice runtime, no ONNX local embeddings, and no optional automation workers.

Enable voice when needed:

```bash
docker compose -f docker-compose.yml -f docker-compose.voice.yml up -d --build
```

Enable ONNX embeddings when needed:

```bash
docker compose -f docker-compose.yml -f docker-compose.onnx.yml up -d
```

Enable optional automation/integration services when needed:

```bash
docker compose -f docker-compose.yml -f docker-compose.automation.yml up -d
```

3) Open the UI:

- `http://localhost:5080/ui`

Smoke test the copilot endpoint (does not require TTS):

```bash
curl -H "Content-Type: application/json" \
  -d '{"text":"Salut ! Tu peux m\u0027aider ?","sessionId":"demo","speak":false}' \
  http://localhost:5080/api/voice/copilot
```

PowerShell (avoids `curl` alias issues):

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/voice/copilot -ContentType "application/json" -Body '{"text":"Salut ! Tu peux m\u0027aider ?","sessionId":"demo","speak":false}'
```

Optional: prefetch the models (best-effort; large downloads):

```bash
docker compose exec infernal-hierarchy /opt/voice-venv/bin/python /app/voice/download_voice_models.py
```

Edit `docker-compose.yml` / `docker-compose.voice.yml` and the `runtime-voice` target in `Dockerfile` if you want to change defaults:

- STT model: `VoiceTranscription__Arguments__4` (default `large-v3-turbo`)
- TTS voice: `TextToSpeech__Arguments__6` (default `af_heart`)

---

### 5️⃣ **(Optional) Enable OTLP Export**

Edit `src/InfernalHierarchy.Host/appsettings.json`:

```json
{
  "OpenTelemetry": {
    "Exporters": {
      "Otlp": {
        "Enabled": true,
        "Endpoint": "http://localhost:4317"
      }
    }
  }
}
```

---

### 6️⃣ **Run the Application**

```bash
# Clean and rebuild
dotnet clean
dotnet build

# Run the application
dotnet run --project src/InfernalHierarchy.Host
```

**Expected**: Application starts without errors, Serilog logs confirm services initialized.

---

## 🧪 Testing Advanced Features

### Test Vector Search

1. Enable `VectorMemoryOptions.Enabled = true`
2. Start Qdrant: `docker compose up -d qdrant`
3. Run the app
4. Use Telegram or memory tools to store facts
5. Query similar facts using the tool `read_memory` with a `query` (it will prefer semantic/vector search when vector memory is enabled).
  - Force keyword mode: pass `mode=keyword`
  - Force semantic mode: pass `mode=semantic`

**Verification**: Check `http://localhost:6333/collections` - you should see `infernal_facts` collection.

**Opt-in live integration test (Qdrant roundtrip)**:

```bash
# Start Qdrant first (docker compose up -d qdrant)
$env:INFERNAL_LIVE_QDRANT=1
dotnet test .\tests\InfernalHierarchy.Memory.Tests\InfernalHierarchy.Memory.Tests.csproj -c Release --filter FullyQualifiedName~VectorMemoryServiceLiveQdrantTests
```

### Test Multi-Model LLM

1. Ensure Ollama is running and the model is present:
  - `ollama list`
2. Run the app
3. Send tasks with different complexities
4. Check logs for model selection:
  - "Using model qwen3:8b"

**Verification**: `TokenUsageTracker` will log usage statistics.

### Test Event Sourcing

1. Run the app with any configuration
2. Create agents, execute tasks
3. Check `./events/events_<agentId>.jsonl` files
4. Verify JSON event logs with timestamps

**Verification**: Each agent action creates an event record.

### Runbook: Memory Pruning (Safe Defaults + Rollback)

**What it does** (when enabled):
- Deletes **Facts** older than `RetentionDays` *and* with confidence `< MinConfidenceThreshold`.
- Deletes **Completed Tasks** older than `RetentionDays`.
- Optionally **archives Decisions** older than `RetentionDays` into JSON files in `ArchivePath`, then deletes them from LiteDB.

**Safety defaults (recommended)**:
- Keep `MemoryPruningOptions.Enabled = false` until you’re ready.
- When you first enable it, keep `MemoryPruningOptions.DryRun = true` (no deletes/archives, logs only).
- Use `MaxDeletesPerRun` as a safety cap for production rollouts.

**Enable (dry-run first)**:
1. Stop the Host.
2. Backup LiteDB: copy `data/infernal.db` to a safe location.
3. Set:
  - `MemoryPruningOptions.Enabled = true`
  - `MemoryPruningOptions.DryRun = true`
  - Optional for testing: `PruningIntervalHours = 0.016` (≈ 1 minute)
  - Optional safety: set `MaxDeletesPerRun = 50`
4. Start the Host and watch logs for: `Memory pruning dry-run complete - would remove ...`.

**Apply changes (actual prune)**:
1. Keep your backup in place.
2. Set `MemoryPruningOptions.DryRun = false`.
3. (Optional) Enable archival: `EnableArchival = true` and confirm `ArchivePath` (default `./archive/memory`).
4. Start the Host and verify:
  - Logs: `Memory pruning complete - removed ...`
  - If archival enabled: JSON files are created under `ArchivePath`.

**Rollback**:
1. Stop the Host.
2. Restore the backed-up `data/infernal.db`.
3. Set `MemoryPruningOptions.Enabled = false` (and keep `DryRun = true` for next time).

---

## 📊 Monitoring

### Check Token Usage

```bash
# In your code or via logs
var stats = tokenUsageTracker.GetOverallStats();
Console.WriteLine($"Total Calls: {stats.TotalCalls}");
Console.WriteLine($"Input Tokens: {stats.InputTokens}");
Console.WriteLine($"Output Tokens: {stats.OutputTokens}");
Console.WriteLine($"Estimated Cost: ${stats.EstimatedCost:F4}");
```

### Check Vector Memory

```bash
# Query Qdrant directly
curl.exe http://localhost:6333/collections/infernal_facts
```

### Check Event Logs

```bash
# View agent events
cat ./events/events_lucifer.jsonl | jq .
```

---

## 🐛 Troubleshooting

### Issue: "Qdrant connection failed"
**Solution**: 
```bash
docker compose restart qdrant
# Or
docker compose up -d qdrant
```

### Issue: "Ollama model not found"
**Solution**:
```bash
curl.exe -s http://localhost:11434/api/tags
# Pull the required model locally
ollama pull qwen3:8b
```

### Issue: "StyleCop warnings everywhere"
**Solution**: StyleCop warnings are expected in the IDE and are non-blocking by default.

If you want a stricter, opt-in quality gate (e.g., in CI), run:
```bash
dotnet build /p:RunAnalyzersDuringBuild=true /p:EnforceCodeStyleInBuild=true
```

Or use the checked-in helper:

```powershell
./scripts/run-analyzers.ps1
./scripts/run-analyzers.ps1 -WarningsAsErrors
```

If you want fewer IDE warnings while iterating, prefer suppressing specific rules in `.editorconfig` instead of turning analyzers off globally.

### Issue: "Memory pruning not working"
**Solution**: 
1. Verify `ISharedMemory` has Delete methods (see step 5)
2. Check `MemoryPruningOptions.Enabled = true`
3. Lower `PruningIntervalHours` for testing
4. Check logs for pruning service activity

---

## 📚 Additional Documentation

- **ADVANCED_FEATURES.md** - Comprehensive guide to all new features
- **COMPLETED.md** - Complete project delivery log and closure state
- **README.md** - Project overview and architecture
- **docker-compose.yml** - Service configuration (InfernalHierarchy, Qdrant, SearXNG)

---

## 🎯 Recommended Order

If you're setting this up for the first time:

1. ✅ Register services in Program.cs (step 1)
2. ✅ Build and verify no errors: `dotnet build`
3. ✅ Start Ollama locally + ensure model is available:
  - `ollama list`
  - `curl.exe -s http://localhost:11434/api/tags`
4. ✅ Start basic app: `dotnet run --project src/InfernalHierarchy.Host`
5. ⏩ Enable Qdrant if you want vector search (steps 3-4)
6. ⏩ Enable memory pruning if you want automatic cleanup (steps 4-5)
7. (Optional) Re-enable complexity-based routing by configuring `LlmOptions.Models` with multiple models.

---

## ✨ What You Get

After completing these steps, you'll have:

✅ **Multi-model intelligence**: Automatic model selection based on task complexity  
✅ **Semantic memory**: Vector search for related facts and memories  
✅ **Cost tracking**: Real-time token usage and cost estimation  
✅ **Audit trail**: Complete event history for compliance and debugging  
✅ **Auto-cleanup**: Intelligent memory pruning to prevent bloat  
✅ **Real-time streaming**: Token-by-token LLM responses  
✅ **Production-ready**: IDE guidance + optional analyzer quality gate

---

## 🆘 Need Help?

1. **Build issues**: Check `dotnet build` output for specific errors
2. **Service issues**: Check Serilog logs in `./logs/` directory
3. **Configuration issues**: Review `appsettings.json` against examples in `ADVANCED_FEATURES.md`
4. **Docker issues**: Run `docker compose logs qdrant` for Qdrant logs

---

**Status**: Ready to integrate! All code is implemented and compiles successfully. Follow steps 1-6 to activate features.
