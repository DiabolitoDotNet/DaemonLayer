
namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class EmailNotificationOptionsValidator : IValidateOptions<EmailNotificationOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailNotificationOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Host)) failures.Add("Email:Host is required when Email:Enabled=true");
        if (options.Port <= 0 || options.Port > 65535) failures.Add("Email:Port must be a valid TCP port");
        if (string.IsNullOrWhiteSpace(options.Username)) failures.Add("Email:Username is required when Email:Enabled=true");
        if (string.IsNullOrWhiteSpace(options.Password)) failures.Add("Email:Password is required when Email:Enabled=true");
        if (string.IsNullOrWhiteSpace(options.FromAddress)) failures.Add("Email:FromAddress is required when Email:Enabled=true");

        if (!string.IsNullOrWhiteSpace(options.DefaultTo) && !options.DefaultTo.Contains('@'))
        {
            failures.Add("Email:DefaultTo must be a valid email address list (comma/semicolon separated)");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
