using InfernalHierarchy.Memory.Configuration;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class VectorMemoryOptionsValidator : IValidateOptions<VectorMemoryOptions>
{
    public ValidateOptionsResult Validate(string? name, VectorMemoryOptions options)
    {
        var errors = new List<string>();

        if (options.QdrantUrl == null || !options.QdrantUrl.IsAbsoluteUri)
        {
            errors.Add("VectorMemoryOptions:QdrantUrl must be an absolute URI");
        }

        if (options.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.CollectionName))
            {
                errors.Add("VectorMemoryOptions:CollectionName is required when vector memory is enabled");
            }

            if (options.VectorDimensions <= 0)
            {
                errors.Add("VectorMemoryOptions:VectorDimensions must be > 0 when vector memory is enabled");
            }
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
