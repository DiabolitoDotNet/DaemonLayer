
namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class UiInterfaceOptionsValidator : IValidateOptions<InfernalHierarchy.Host.Configuration.UiInterfaceOptions>
{
    public ValidateOptionsResult Validate(string? name, InfernalHierarchy.Host.Configuration.UiInterfaceOptions options)
    {
        // Always valid; only local-only toggle is enforced at runtime.
        return ValidateOptionsResult.Success;
    }
}
