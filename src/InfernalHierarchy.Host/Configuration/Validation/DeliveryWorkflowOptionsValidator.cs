namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class DeliveryWorkflowOptionsValidator : IValidateOptions<DeliveryWorkflowOptions>
{
    public ValidateOptionsResult Validate(string? name, DeliveryWorkflowOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            failures.Add("DeliveryWorkflow:RootDirectory is required when DeliveryWorkflow:Enabled=true");
        }

        if (options.DefaultTimeoutMs <= 0)
        {
            failures.Add("DeliveryWorkflow:DefaultTimeoutMs must be > 0");
        }

        if (options.MaxOutputBytes <= 0)
        {
            failures.Add("DeliveryWorkflow:MaxOutputBytes must be > 0");
        }

        if (options.MaxDiscoveryFiles <= 0)
        {
            failures.Add("DeliveryWorkflow:MaxDiscoveryFiles must be > 0");
        }

        foreach (var (adapterId, adapter) in options.Adapters)
        {
            if (!adapter.Enabled)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(adapter.DeployExecutable) && string.IsNullOrWhiteSpace(adapter.RollbackExecutable))
            {
                failures.Add($"DeliveryWorkflow:Adapters:{adapterId} must configure at least DeployExecutable or RollbackExecutable when enabled");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}