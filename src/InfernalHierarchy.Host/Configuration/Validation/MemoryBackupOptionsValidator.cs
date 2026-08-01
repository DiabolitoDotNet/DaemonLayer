namespace InfernalHierarchy.Host.Configuration.Validation;

public sealed class MemoryBackupOptionsValidator : IValidateOptions<MemoryBackupOptions>
{
    public ValidateOptionsResult Validate(string? name, MemoryBackupOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DirectoryPath))
        {
            errors.Add("MemoryBackup:DirectoryPath is required when backups are enabled");
        }

        if (options.IntervalHours <= 0)
        {
            errors.Add("MemoryBackup:IntervalHours must be > 0");
        }

        if (options.MaxBackupFiles <= 0)
        {
            errors.Add("MemoryBackup:MaxBackupFiles must be > 0");
        }

        if (options.MaxBackupAgeDays <= 0)
        {
            errors.Add("MemoryBackup:MaxBackupAgeDays must be > 0");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}