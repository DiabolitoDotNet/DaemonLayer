using InfernalHierarchy.Memory.Configuration;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class OnnxEmbeddingOptionsValidator : IValidateOptions<OnnxEmbeddingOptions>
{
    public ValidateOptionsResult Validate(string? name, OnnxEmbeddingOptions options)
    {
        var errors = new List<string>();

        if (options.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.ModelPath))
            {
                errors.Add("OnnxEmbeddingOptions:ModelPath is required when ONNX embeddings are enabled");
            }

            if (string.IsNullOrWhiteSpace(options.TokenizerPath))
            {
                errors.Add("OnnxEmbeddingOptions:TokenizerPath is required when ONNX embeddings are enabled");
            }
        }

        if (options.MaxSequenceLength <= 0)
        {
            errors.Add("OnnxEmbeddingOptions:MaxSequenceLength must be > 0");
        }

        if (options.EmbeddingDimension <= 0)
        {
            errors.Add("OnnxEmbeddingOptions:EmbeddingDimension must be > 0");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
