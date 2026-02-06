using InfernalHierarchy.Host.Configuration;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class WebSocketInterfaceOptionsValidator : IValidateOptions<WebSocketInterfaceOptions>
{
    public ValidateOptionsResult Validate(string? name, WebSocketInterfaceOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (options.MaxClientMessageBytes <= 0)
        {
            failures.Add("WebSockets:MaxClientMessageBytes must be > 0");
        }

        if (options.KeepAliveSeconds <= 0)
        {
            failures.Add("WebSockets:KeepAliveSeconds must be > 0");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
