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
5. Query similar facts using `SearchSimilarAsync`

**Verification**: Check `http://localhost:6333/collections` - you should see `infernal_facts` collection.

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

### Test Memory Pruning

1. Enable `MemoryPruningOptions.Enabled = true`
2. Create old facts with low confidence
3. Wait 24 hours OR temporarily change `PruningIntervalHours: 0.016` (1 minute)
4. Check logs for pruning activity
5. Verify `./archives/` directory for archived decisions

**Verification**: Old data is removed from LiteDB, saved to archives.

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
