namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class EmailInboxQueryOptionsValidator : IValidateOptions<EmailInboxQueryOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailInboxQueryOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Host)) failures.Add("EmailInbox:Host is required when EmailInbox:Enabled=true");
        if (options.Port <= 0 || options.Port > 65535) failures.Add("EmailInbox:Port must be a valid TCP port");
        if (string.IsNullOrWhiteSpace(options.Username)) failures.Add("EmailInbox:Username is required when EmailInbox:Enabled=true");
        if (string.IsNullOrWhiteSpace(options.Password)) failures.Add("EmailInbox:Password is required when EmailInbox:Enabled=true");
        if (options.MaxResults <= 0 || options.MaxResults > 100) failures.Add("EmailInbox:MaxResults must be between 1 and 100");
        if (options.TimeoutMs <= 0 || options.TimeoutMs > 120000) failures.Add("EmailInbox:TimeoutMs must be between 1 and 120000");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
