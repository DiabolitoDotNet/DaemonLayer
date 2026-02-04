# ONNX Embedding Models

This directory contains ONNX models for sentence embeddings used by the VectorMemoryService.

## Recommended Model: all-MiniLM-L6-v2

**Embedding Dimension**: 384  
**Model Size**: ~90 MB  
**Performance**: Fast, suitable for semantic search

### Download Instructions

#### Option 1: Using Optimum CLI (Recommended)

```bash
# Install optimum and onnxruntime
pip install optimum[exporters] onnxruntime

# Convert and export model to ONNX
optimum-cli export onnx --model sentence-transformers/all-MiniLM-L6-v2 --task feature-extraction ./models/sentence-transformers/
```

#### Option 2: Using Hugging Face Hub

```bash
# Install huggingface_hub
pip install huggingface_hub

# Download pre-converted ONNX model (if available)
python -c "from huggingface_hub import snapshot_download; snapshot_download('sentence-transformers/all-MiniLM-L6-v2', local_dir='./models/sentence-transformers')"
```

#### Option 3: Manual Conversion with Python

```python
from transformers import AutoTokenizer, AutoModel
from optimum.onnxruntime import ORTModelForFeatureExtraction
import os

model_name = "sentence-transformers/all-MiniLM-L6-v2"
output_dir = "./models/sentence-transformers"

# Load and convert model
model = ORTModelForFeatureExtraction.from_pretrained(model_name, export=True)
tokenizer = AutoTokenizer.from_pretrained(model_name)

# Save
os.makedirs(output_dir, exist_ok=True)
model.save_pretrained(output_dir)
tokenizer.save_pretrained(output_dir)

print(f"Model saved to {output_dir}")
```

### Expected Directory Structure

```
./models/sentence-transformers/
├── model.onnx                 # ONNX model file
├── tokenizer.json             # Tokenizer configuration
├── config.json                # Model configuration
├── vocab.txt                  # Vocabulary (optional)
└── special_tokens_map.json   # Special tokens (optional)
```

## Alternative Models

### all-mpnet-base-v2 (Higher Quality)

**Embedding Dimension**: 768  
**Model Size**: ~420 MB  
**Performance**: Better quality, slower

```bash
optimum-cli export onnx --model sentence-transformers/all-mpnet-base-v2 --task feature-extraction ./models/sentence-transformers/
```

Update `appsettings.json`:
```json
"OnnxEmbeddingOptions": {
  "Enabled": true,
  "ModelPath": "./models/sentence-transformers/model.onnx",
  "TokenizerPath": "./models/sentence-transformers/tokenizer.json",
  "MaxSequenceLength": 128,
  "EmbeddingDimension": 768
}
```

### MiniLM-L12-v2 (Balanced)

**Embedding Dimension**: 384  
**Model Size**: ~120 MB  
**Performance**: Balanced quality/speed

```bash
optimum-cli export onnx --model sentence-transformers/all-MiniLM-L12-v2 --task feature-extraction ./models/sentence-transformers/
```

## Configuration

Enable ONNX embeddings in `appsettings.json`:

```json
"OnnxEmbeddingOptions": {
  "Enabled": true,
  "ModelPath": "./models/sentence-transformers/model.onnx",
  "TokenizerPath": "./models/sentence-transformers/tokenizer.json",
  "MaxSequenceLength": 128,
  "EmbeddingDimension": 384
},
"VectorMemoryOptions": {
  "QdrantUrl": "http://localhost:6333",
  "CollectionName": "infernal_facts",
  "VectorDimensions": 384,
  "Enabled": true
}
```

## Fallback Behavior

If ONNX models are not found or `Enabled: false`, the system automatically falls back to deterministic hash-based embeddings. This allows the system to run without downloading models, but with reduced semantic search quality.

## Testing

Test embeddings after setup:

```bash
# Start the application
dotnet run --project src/InfernalHierarchy.Host

# Check logs for:
# ✅ Loaded ONNX model from ./models/sentence-transformers/model.onnx
# ✅ Loaded tokenizer from ./models/sentence-transformers/tokenizer.json
```

## Performance Notes

- **all-MiniLM-L6-v2**: ~5ms per embedding (CPU)
- **all-mpnet-base-v2**: ~15ms per embedding (CPU)
- GPU acceleration: Use ONNX Runtime with DirectML/CUDA for 10x speedup

## Troubleshooting

### Model Not Found
- Ensure model files exist in the configured path
- Check file permissions
- Verify `ModelPath` and `TokenizerPath` in configuration

### Out of Memory
- Reduce `MaxSequenceLength` to 64 or 32
- Use smaller model (all-MiniLM-L6-v2 instead of all-mpnet-base-v2)

### Slow Performance
- Enable GPU acceleration (requires ONNX Runtime GPU packages)
- Reduce batch size
- Use smaller model

## References

- [Sentence Transformers](https://www.sbert.net/)
- [ONNX Runtime](https://onnxruntime.ai/)
- [Hugging Face Optimum](https://huggingface.co/docs/optimum/index)
