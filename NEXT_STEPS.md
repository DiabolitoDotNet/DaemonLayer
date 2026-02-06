# 🚀 Next Steps - Getting Advanced Features Running

**Last Updated**: February 4, 2026  
**Status**: Solution builds and tests pass in Release; advanced features are integrated and ready to run (some require local dependencies)

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

**Build Status**: ✅ Builds clean by default (analyzers are enabled for IDE/live analysis; build-time analyzers are opt-in)

**Test Status**: ✅ `dotnet test -c Release` passes solution-wide (some integration-only tests are skipped by design)

---

## 🎯 What You Need To Do

### 1️⃣ **Configure Secrets** (REQUIRED)

- Set Telegram bot token (and optionally AllowedUserIds) using user-secrets or a secrets file.
- Ensure `src/InfernalHierarchy.Host/appsettings.json` has non-empty `Telegram:BotToken` at runtime.

---

### 2️⃣ **Start Ollama + Pull Models** (REQUIRED for LLM)

Open a terminal and run:

```bash
# Pull all 4 configured models
ollama pull llama3.1:8b      # Medium tasks (default)
ollama pull gemma:2b          # Simple tasks
ollama pull qwen:32b          # Complex tasks
ollama pull deepseek-coder:6.7b  # Expert/code tasks
```

**Time Required**: ~10-30 minutes depending on internet speed  
**Disk Space**: ~25 GB total for all 4 models

**Optional**: If you only want to test with one model initially, just pull `llama3.1:8b`.

---

### 3️⃣ **Start Qdrant (Docker)** (REQUIRED for vector search)

```bash
# Start Qdrant container
docker-compose up -d qdrant

# Verify it's running
curl http://localhost:6333/collections
```

**Expected Response**: `{"result":{"collections":[]},"status":"ok","time":0.000...}`

**Disk Space**: Minimal (~100MB for Docker image)

**Tip**: The repo also ships a full-stack compose including the Host + Qdrant + SearXNG in `docker-compose.yml`.

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

---

### 4️⃣c **(Optional) Enable Voice (STT/TTS) via Docker**

The embedded UI includes a Voice panel that calls:

- `POST /api/voice/transcribe` (STT)
- `POST /api/voice/speak` (TTS)

For interactive/local usage, the recommended “optimized enough” setup is:

- **TTS**: Piper.Net (in-process) with the model cached after first use
- **STT**: whisper.cpp CLI invoked per request (low volume)

1) Put models on disk:

- Whisper model under `./models/whisper` (example: `ggml-base.en.bin`)
- Piper ONNX voice under `./models/piper` (example: `voice.onnx`)

2) Start the stack using the voice override:

```bash
docker compose -f docker-compose.yml -f docker-compose.voice.yml up -d --build
```

3) Open the UI:

- `http://localhost:5080/ui`

Edit `docker-compose.voice.yml` if your model filenames differ:

- `VoiceTranscription__Arguments__1` → Whisper model path
- `TextToSpeech__PiperVoicePath` → Piper voice path
- `TextToSpeech__PiperWarmupAtStartup` → pre-load + warm the model at container startup (recommended)

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
2. Start Qdrant: `docker-compose up -d qdrant`
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

1. Pull Ollama models (see step 2)
2. Run the app
3. Send tasks with different complexities
4. Check logs for model selection:
   - `"Using model gemma:2b for Simple task"`
   - `"Using model llama3.1:8b for Medium task"`

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
curl http://localhost:6333/collections/infernal_facts
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
docker-compose restart qdrant
# Or
docker-compose up -d qdrant
```

### Issue: "Ollama model not found"
**Solution**:
```bash
ollama list  # Check available models
ollama pull llama3.1:8b  # Pull missing model
```

### Issue: "StyleCop warnings everywhere"
**Solution**: StyleCop warnings are expected in the IDE and are non-blocking by default.

If you want a stricter, opt-in quality gate (e.g., in CI), run:
```bash
dotnet build /p:RunAnalyzersDuringBuild=true /p:EnforceCodeStyleInBuild=true
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
- **TODO.md** - Complete project status and roadmap
- **README.md** - Project overview and architecture
- **docker-compose.yml** - Service configuration (Ollama, Qdrant, SearXNG)

---

## 🎯 Recommended Order

If you're setting this up for the first time:

1. ✅ Register services in Program.cs (step 1)
2. ✅ Build and verify no errors: `dotnet build`
3. ✅ Pull at least one Ollama model: `ollama pull llama3.1:8b`
4. ✅ Start basic app: `dotnet run --project src/InfernalHierarchy.Host`
5. ⏩ Enable Qdrant if you want vector search (steps 3-4)
6. ⏩ Enable memory pruning if you want automatic cleanup (steps 4-5)
7. ⏩ Pull additional models if you want complexity-based routing (step 2)

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
4. **Docker issues**: Run `docker-compose logs qdrant` for Qdrant logs

---

**Status**: Ready to integrate! All code is implemented and compiles successfully. Follow steps 1-6 to activate features.
